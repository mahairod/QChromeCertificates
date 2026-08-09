#include "BrowserDefinition.h"
#include <QStandardPaths>
#include <QFileInfo>
#include <QDir>
#ifdef Q_OS_WIN
#include <windows.h>
#endif

QList<BrowserDefinition> BrowserDefinition::all() {
    return {
        {"chrome","Chrome",R"(Software\Policies\Google\Chrome)",
         "/etc/opt/chrome/policies","com.google.Chrome","google-chrome","chrome://policy/"},
        {"edge","Edge",R"(Software\Policies\Microsoft\Edge)",
         "/etc/microsoft-edge/policies","com.microsoft.Edge","microsoft-edge","edge://policy/"},
        {"brave","Brave",R"(Software\Policies\BraveSoftware\Brave)",
         "/etc/brave/policies","com.brave.Browser","brave-browser","brave://policy/"},
        {"chromium","Chromium",R"(Software\Policies\Chromium)",
         "/etc/chromium/policies","org.chromium.Chromium","chromium","chrome://policy/"}
    };
}
BrowserDefinition BrowserDefinition::find(const QString &id) {
    for (const auto &b : all()) if (b.id.compare(id, Qt::CaseInsensitive)==0) return b;
    return all().first();
}
QString BrowserDefinition::resolveExecutable() const {
#ifdef Q_OS_WIN
    if (id=="chrome") return "chrome.exe";
    if (id=="edge") return "msedge.exe";
    if (id=="brave") return "brave.exe";
    if (id=="chromium") return "chrome.exe";
#endif
    const QStringList candidates = { executable, id };
    for (const auto &c : candidates) {
        const QString p = QStandardPaths::findExecutable(c);
        if (!p.isEmpty()) return p;
    }
    return executable;
}
