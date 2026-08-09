#include "LinuxPolicyStore.h"
#include <QFile>
#include <QDir>
#include <QJsonDocument>
#include <QJsonObject>
#include <QStandardPaths>
#include <QSaveFile>
#include <QFileInfo>
#include <stdexcept>

LinuxPolicyStore::LinuxPolicyStore(const BrowserDefinition &b, PolicyScope s, const QString &overrideRoot)
    : browser_(b), scope_(s), root_(overrideRoot) {
    if(root_.isEmpty()) root_=qEnvironmentVariable("QCC_LINUX_POLICY_ROOT");
    if(root_.isEmpty()) root_=browser_.linuxPolicyRoot;
}

QString LinuxPolicyStore::filePath() const {
    return root_ + "/" + (scope_==PolicyScope::Managed ? "managed" : "recommended") + "/qchrome-certificates.json";
}

PolicyState LinuxPolicyStore::load() {
    PolicyState s;
    QFile f(filePath()); if(!f.open(QIODevice::ReadOnly)) return s;
    auto doc=QJsonDocument::fromJson(f.readAll());
    if(!doc.isObject()) throw std::runtime_error("Chrome policy file must contain a JSON object.");
    auto o=doc.object();
    if(o.contains("CAPlatformIntegrationEnabled")) s.platformIntegrationEnabled=o["CAPlatformIntegrationEnabled"].toBool();
    if(o.contains("CACertificatesWithConstraints")) {
        if(!o["CACertificatesWithConstraints"].isArray()) throw std::runtime_error("CACertificatesWithConstraints must be an array.");
        s.entries=entriesFromJson(o["CACertificatesWithConstraints"].toArray());
    }
    return s;
}

void LinuxPolicyStore::save(const PolicyState &s) {
    const QString dir=QFileInfo(filePath()).absolutePath();
    if(!QDir().mkpath(dir)) throw std::runtime_error("Cannot create Chrome policy directory.");
    QJsonObject o;
    if(s.platformIntegrationEnabled.has_value()) o["CAPlatformIntegrationEnabled"]=*s.platformIntegrationEnabled;
    if(!s.entries.isEmpty()) o["CACertificatesWithConstraints"]=entriesToJson(s.entries);
    else if(!s.platformIntegrationEnabled.has_value()) {
        QFile::remove(filePath()); return;
    }
    QSaveFile f(filePath()); if(!f.open(QIODevice::WriteOnly)) throw std::runtime_error(("Cannot write "+filePath()).toStdString());
    f.write(QJsonDocument(o).toJson(QJsonDocument::Indented));
    if(!f.commit()) throw std::runtime_error("Cannot commit Chrome policy file.");
}

QString LinuxPolicyStore::statusText() const {
    return "Linux: " + filePath();
}

bool LinuxPolicyStore::needsElevation() const {
    return filePath().startsWith("/etc/");
}
