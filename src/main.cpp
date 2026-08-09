#include <QApplication>
#include <QCommandLineParser>
#include <QMessageBox>
#include "MainWindow.h"
#include "ConstraintsDialog.h"
#include "PolicyModel.h"
#include "PolicyBackup.h"
#ifdef Q_OS_WIN
#include "WindowsPolicyStore.h"
#endif
int main(int argc,char**argv){
    QApplication app(argc,argv);QApplication::setApplicationName("QChromeCertificates");QApplication::setApplicationVersion(QCC_VERSION);
    QCommandLineParser p;p.setApplicationDescription("Cross-platform Chrome certificate policy manager");
    QCommandLineOption self("self-test");QCommandLineOption reg("registry-path","Windows test registry path","path");p.addOption(self);p.addOption(reg);p.process(app);
    if(p.isSet(self)){
        try{
            auto d=ConstraintsDialog::parseDomains("GOSUSLUGI.RU\n.пример.рф");if(d.size()!=2||d[0]!="gosuslugi.ru"||!d[1].startsWith(".xn--"))return 1;
            auto e=ConstraintsDialog::expandSubdomains({"example.ru"});if(e.size()!=2||e[1]!=".example.ru")return 1;
            auto c=ConstraintsDialog::collapseSubdomainPairs(e);if(c.size()!=1||c[0]!="example.ru")return 1;
            auto cidr=ConstraintsDialog::parseCidrs("10.1.1.5/24\n2001:db8::1/64");if(cidr.size()!=2||cidr[0]!="10.1.1.0/24"||cidr[1]!="2001:db8::/64")return 1;
            CertificatePolicyEntry ce{"AQID",{"gosuslugi.ru",".gosuslugi.ru"},{"10.1.1.0/24"}};PolicyState s;s.platformIntegrationEnabled=false;s.entries={ce};
            auto j=serializeEntries(s.entries);if(!j.contains("\"certificate\":\"AQID\""))return 1;
            qInfo("Self-test passed.");return 0;
        }catch(const std::exception&e){qCritical("Self-test failed: %s",e.what());return 1;}
    }
    AppSettings settings=AppSettings::load();QString testPath=p.value(reg);MainWindow w(settings,testPath);w.show();return app.exec();
}
