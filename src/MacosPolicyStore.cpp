#include "MacosPolicyStore.h"
#include <QFile>
#include <QDir>
#include <QXmlStreamReader>
#include <QXmlStreamWriter>
#include <QSaveFile>
#include <stdexcept>

static void writeString(QXmlStreamWriter& w,const QString& v) {
	w.writeTextElement("string",v);
}
MacPolicyStore::MacPolicyStore(const BrowserDefinition& b, PolicyScope s):browser_(b),scope_(s) {}
QString MacPolicyStore::filePath() const {
	if(scope_==PolicyScope::Managed) return "/Library/Managed Preferences/"+browser_.macBundleId+".plist";
	return QDir::homePath()+"/Library/Preferences/"+browser_.macBundleId+".plist";
}
static void skip(QXmlStreamReader& r) {
	if(r.isStartElement()) r.skipCurrentElement();
}
PolicyState MacPolicyStore::load() {
	PolicyState s;
	QFile f(filePath());
	if(!f.open(QIODevice::ReadOnly)) return s;
	QXmlStreamReader r(&f);
	QString key;
	bool inDict=false;
	while(!r.atEnd()) {
		r.readNext();
		if(r.isStartElement() && r.name()=="key") {
			key=r.readElementText();
			continue;
		}
		if(r.isStartElement() && key=="CAPlatformIntegrationEnabled" && (r.name()=="true"||r.name()=="false")) {
			s.platformIntegrationEnabled=(r.name().data()=="false");
			key.clear();
			continue;
		}
		if(r.isStartElement() && key=="CACertificatesWithConstraints" && r.name()=="array") {
			while(!r.atEnd()) {
				r.readNext();
				if(r.isEndElement()&&r.name()=="array")break;
				if(r.isStartElement()&&r.name()=="dict") {
					CertificatePolicyEntry e;
					QString k;
					while(!r.atEnd()) {
						r.readNext();
						if(r.isEndElement()&&r.name()=="dict")break;
						if(r.isStartElement()&&r.name()=="key") {
							k=r.readElementText();
							continue;
						}
						if(r.isStartElement()&&k=="certificate"&&r.name()=="string") {
							e.certificateBase64=r.readElementText();
							k.clear();
						} else if(r.isStartElement()&&k=="constraints"&&r.name()=="dict") {
							QString ck;
							while(!r.atEnd()) {
								r.readNext();
								if(r.isEndElement()&&r.name()=="dict")break;
								if(r.isStartElement()&&r.name()=="key") {
									ck=r.readElementText();
									continue;
								}
								if(r.isStartElement()&&r.name()=="array"&&(ck=="permitted_dns_names"||ck=="permitted_cidrs")) {
									QStringList* dst=(ck=="permitted_dns_names"?&e.dnsNames:&e.cidrs);
									while(!r.atEnd()) {
										r.readNext();
										if(r.isEndElement()&&r.name()=="array")break;
										if(r.isStartElement()&&r.name()=="string") dst->append(r.readElementText());
									}
									ck.clear();
								}
							}
							k.clear();
						}
					}
					s.entries.append(e);
				}
			}
			key.clear();
		}
	}
	if(r.hasError()) throw std::runtime_error("Invalid macOS plist.");
	return s;
}
void MacPolicyStore::save(const PolicyState& s) {
	QDir().mkpath(QFileInfo(filePath()).absolutePath());
	QSaveFile f(filePath());
	if(!f.open(QIODevice::WriteOnly)) throw std::runtime_error(("Cannot write "+filePath()).toStdString());
	QXmlStreamWriter w(&f);
	w.setAutoFormatting(true);
	w.writeStartDocument();
	w.writeDTD("<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">");
	w.writeStartElement("plist");
	w.writeAttribute("version","1.0");
	w.writeStartElement("dict");
	if(s.platformIntegrationEnabled.has_value()) {
		w.writeTextElement("key","CAPlatformIntegrationEnabled");
		w.writeStartElement(*s.platformIntegrationEnabled?"true":"false");
		w.writeEndElement();
	}
	if(!s.entries.isEmpty()) {
		w.writeTextElement("key","CACertificatesWithConstraints");
		w.writeStartElement("array");
		for(const auto& e:s.entries) {
			w.writeStartElement("dict");
			w.writeTextElement("key","certificate");
			writeString(w,e.certificateBase64);
			w.writeTextElement("key","constraints");
			w.writeStartElement("dict");
			if(!e.dnsNames.isEmpty()) {
				w.writeTextElement("key","permitted_dns_names");
				w.writeStartElement("array");
				for(auto& x:e.dnsNames)writeString(w,x);
				w.writeEndElement();
			}
			if(!e.cidrs.isEmpty()) {
				w.writeTextElement("key","permitted_cidrs");
				w.writeStartElement("array");
				for(auto& x:e.cidrs)writeString(w,x);
				w.writeEndElement();
			}
			w.writeEndElement();
			w.writeEndElement();
		}
		w.writeEndElement();
	}
	w.writeEndElement();
	w.writeEndElement();
	w.writeEndDocument();
	if(!f.commit()) throw std::runtime_error("Cannot commit plist.");
}
QString MacPolicyStore::statusText() const {
	return "macOS: "+filePath();
}
bool MacPolicyStore::needsElevation() const {
	return scope_==PolicyScope::Managed;
}
