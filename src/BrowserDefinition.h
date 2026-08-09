#pragma once
#include <QString>
#include <QList>

struct BrowserDefinition {
	QString id;
	QString name;
	QString windowsRegistryPath;
	QString linuxPolicyRoot;
	QString macBundleId;
	QString executable;
	QString policyUrl;

	QString resolveExecutable() const;
	static QList<BrowserDefinition> all();
	static BrowserDefinition find(const QString &id);
};
