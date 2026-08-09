#include "WindowsPolicyStore.h"
#ifdef Q_OS_WIN
#include <windows.h>
#include <shellapi.h>
#include <sddl.h>
#include <QTemporaryFile>
#include <QProcess>
#include <QFile>
#include <QTextStream>
#include <QCoreApplication>
#include <QDir>
#endif
#include <QJsonDocument>
#include <QRegularExpression>
#include <stdexcept>

#ifdef Q_OS_WIN
static HKEY openPath(HKEY root,const QString& path,REGSAM sam=KEY_READ){
	HKEY k=nullptr; if(RegOpenKeyExW(root,(LPCWSTR)path.utf16(),0,sam,&k)!=ERROR_SUCCESS)return nullptr; return k;
}
static QString readString(HKEY k,const wchar_t *name){
	DWORD type=0,size=0; if(RegQueryValueExW(k,name,nullptr,&type,nullptr,&size)!=ERROR_SUCCESS||type!=REG_SZ)return {};
	QByteArray b(size,0); if(RegQueryValueExW(k,name,nullptr,&type,(LPBYTE)b.data(),&size)!=ERROR_SUCCESS)return {};
	return QString::fromWCharArray(reinterpret_cast<const wchar_t*>(b.constData()));
}
static bool readDword(HKEY k,const wchar_t*name,std::optional<bool>&out){
	DWORD type=0,v=0,size=sizeof(v); if(RegQueryValueExW(k,name,nullptr,&type,(LPBYTE)&v,&size)!=ERROR_SUCCESS)return false;
	if(type==REG_DWORD){out=(v!=0);return true;} return false;
}
#endif

WindowsPolicyStore::WindowsPolicyStore(const BrowserDefinition&b,const QString&testPath):browser_(b),path_(testPath.isEmpty()?b.windowsRegistryPath:testPath){}
PolicyState WindowsPolicyStore::load(){
	PolicyState s;
#ifdef Q_OS_WIN
	HKEY k=openPath(HKEY_CURRENT_USER,path_); if(!k)return s;
	readDword(k,L"CAPlatformIntegrationEnabled",s.platformIntegrationEnabled);
	QString json=readString(k,L"CACertificatesWithConstraints"); RegCloseKey(k);
	if(!json.trimmed().isEmpty()){
		auto doc=QJsonDocument::fromJson(json.toUtf8()); if(!doc.isArray())throw std::runtime_error("CACertificatesWithConstraints must contain a JSON array.");
		s.entries=entriesFromJson(doc.array());
	}
#else
	Q_UNUSED(s); throw std::runtime_error("Windows policy store used on non-Windows.");
#endif
	return s;
}
void WindowsPolicyStore::save(const PolicyState&s){
#ifdef Q_OS_WIN
	HKEY k=nullptr; if(RegCreateKeyExW(HKEY_CURRENT_USER,(LPCWSTR)path_.utf16(),0,nullptr,0,KEY_SET_VALUE,nullptr,&k,nullptr)!=ERROR_SUCCESS)
		throw std::runtime_error("Cannot write HKCU policy key.");
	if(s.platformIntegrationEnabled.has_value()){DWORD v=*s.platformIntegrationEnabled?1:0;RegSetValueExW(k,L"CAPlatformIntegrationEnabled",0,REG_DWORD,(BYTE*)&v,sizeof(v));}
	else RegDeleteValueW(k,L"CAPlatformIntegrationEnabled");
	if(s.entries.isEmpty()) RegDeleteValueW(k,L"CACertificatesWithConstraints");
	else {QByteArray j=serializeEntries(s.entries);RegSetValueExW(k,L"CACertificatesWithConstraints",0,REG_SZ,(BYTE*)QString::fromUtf8(j).utf16(),(j.size()+1)*sizeof(wchar_t));}
	RegCloseKey(k);
#else
	Q_UNUSED(s); throw std::runtime_error("Windows policy store used on non-Windows.");
#endif
}
QString WindowsPolicyStore::statusText()const{return "HKCU\\"+path_;}
bool WindowsPolicyStore::machinePolicyPresent()const{
#ifdef Q_OS_WIN
	HKEY k=openPath(HKEY_LOCAL_MACHINE,path_);if(!k)return false;
	bool p=RegQueryValueExW(k,L"CAPlatformIntegrationEnabled",nullptr,nullptr,nullptr,nullptr)==ERROR_SUCCESS ||
		   RegQueryValueExW(k,L"CACertificatesWithConstraints",nullptr,nullptr,nullptr,nullptr)==ERROR_SUCCESS;
	RegCloseKey(k);return p;
#else
	return false;
#endif
}

QString buildRegistryFile(const QString&sid,const QString&path,const PolicyState&s){
	if(!QRegularExpression("^S-\\d+(?:-\\d+)+$").match(sid).hasMatch()||path.isEmpty()||path.contains(QRegularExpression("[\\r\\n\\[\\]]")))
		throw std::runtime_error("Invalid registry target.");
	QString out="Windows Registry Editor Version 5.00\r\n\r\n[HKEY_USERS\\"+sid+"\\"+path+"]\r\n";
	if(s.platformIntegrationEnabled.has_value()) out+="\"CAPlatformIntegrationEnabled\"=dword:"+QString(*s.platformIntegrationEnabled?"00000001":"00000000")+"\r\n";
	else out+="\"CAPlatformIntegrationEnabled\"=-\r\n";
	if(s.entries.isEmpty()) out+="\"CACertificatesWithConstraints\"=-\r\n";
	else {QString j=QString::fromUtf8(serializeEntries(s.entries));j.replace("\\","\\\\").replace("\"","\\\"");out+="\"CACertificatesWithConstraints\"=\""+j+"\"\r\n";}
	return out+"\r\n";
}
bool saveRegistryElevated(const QString&path,const PolicyState&s){
#ifdef Q_OS_WIN
	HANDLE tok=nullptr;OpenProcessToken(GetCurrentProcess(),TOKEN_QUERY,&tok);
	QByteArray sidA;QString sid;
	if(tok){DWORD len=0;GetTokenInformation(tok,TokenUser,nullptr,0,&len);QByteArray b(len,0);if(GetTokenInformation(tok,TokenUser,b.data(),len,&len)){auto tu=(TOKEN_USER*)b.data();LPWSTR p=nullptr;if(ConvertSidToStringSidW(tu->User.Sid,&p)){sid=QString::fromWCharArray(p);LocalFree(p);}}CloseHandle(tok);}
	if(sid.isEmpty())throw std::runtime_error("Could not determine current user's SID.");
	QTemporaryFile f(QDir::tempPath()+"/QChromeCertificates-XXXXXX.reg");f.setAutoRemove(false);if(!f.open())throw std::runtime_error("Cannot create temporary registry file.");
	const QString file=f.fileName();f.write(reinterpret_cast<const char*>(u"\xFE\xFF"),2);
	const QString regText = buildRegistryFile(sid,path,s);
	QByteArray utf16(reinterpret_cast<const char*>(regText.utf16()), regText.size()*2);
	f.write(utf16);f.close();
	SHELLEXECUTEINFOW sei{sizeof(sei)};sei.fMask=SEE_MASK_NOCLOSEPROCESS;sei.lpVerb=L"runas";sei.lpFile=L"reg.exe";
	QString args="import \""+QDir::toNativeSeparators(file)+"\" /reg:64";sei.lpParameters=(LPCWSTR)args.utf16();sei.nShow=SW_HIDE;
	if(!ShellExecuteExW(&sei)){QFile::remove(file); if(GetLastError()==ERROR_CANCELLED)return false;throw std::runtime_error("UAC registry import failed.");}
	WaitForSingleObject(sei.hProcess,INFINITE);DWORD code=1;GetExitCodeProcess(sei.hProcess,&code);CloseHandle(sei.hProcess);QFile::remove(file);
	if(code!=0)throw std::runtime_error("Registry import failed.");return true;
#else
	Q_UNUSED(path);Q_UNUSED(s);return false;
#endif
}
