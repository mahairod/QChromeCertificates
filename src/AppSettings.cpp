#include "AppSettings.h"
#include "BrowserDefinition.h"
#include <QCoreApplication>
#include <QFile>
#include <QJsonDocument>
#include <QJsonObject>
#include <QJsonArray>
#include <QLocale>
#include <QDir>
#include <QRegularExpression>

static QString langCodes(bool tails){
	QString patt = "";
	for (const LangPair& lp: allLangs()) {
		patt += lp.first;
		patt += "|";
	}
	patt = patt.left(patt.length()-1);
	if (tails)
		patt = '(' + patt + ")([_-].+)?";
	return patt;
}

static QRegularExpression LANG_CODE_SELECTOR(langCodes(true));
static QRegularExpression LANG_CODE_PATT(langCodes(false));

static QString settingsPath() {
	const auto exe = QCoreApplication::applicationFilePath();
	QFileInfo fi(exe);
	return fi.absolutePath() + "/" + fi.completeBaseName() + "-settings.json";
}
AppSettings AppSettings::load() {
	AppSettings s;
	QString locLang = QLocale::system().name();
	s.language = LANG_CODE_SELECTOR.match(locLang).isValid()  ? locLang : "en";
	QFile f(settingsPath());
	if (!f.open(QIODevice::ReadOnly))
		return s;
	try {
		QByteArray data = f.readAll();
		auto jo = QJsonDocument::fromJson(data);
		auto o = jo.object();

		QString lang = o["language"].toString();
		if (LANG_CODE_PATT.match(lang).hasMatch())
			s.language = lang;

		QString browserId = o["browserId"].toString();

		if (o["browsers"].isArray()) {
			auto brs = o["browsers"].toArray();
			for (const auto& bVal: brs) {
				if (!bVal.isObject()) continue;
				auto b = bVal.toObject();
				BrowserDefinition bd;
				bd.id = 	b["id"].toString();
				bd.name = 	b["name"].toString();
				bd.windowsRegistryPath = b["windowsRegistryPath"].toString();
				bd.linuxPolicyRoot = 	b["linuxPolicyRoot"].toString();
				bd.macBundleId = 		b["macBundleId"].toString();
				bd.executable = 		b["executable"].toString();
				bd.policyUrl = 			b["policyUrl"].toString();

				s.extraBrowsers.append(bd);
				if (bd.id == browserId) {
					s.browserId = browserId;
				}
			}
		}

		if (!BrowserDefinition::find(browserId).id.isEmpty())
			s.browserId = browserId;

		if (o["policyScope"].toString()=="managed" || o["policyScope"].toString()=="recommended")
			s.policyScope = o["policyScope"].toString();

		if (o["windowWidth"].toInt()>=850)
			s.windowWidth = o["windowWidth"].toInt();
		if (o["windowHeight"].toInt()>=520)
			s.windowHeight = o["windowHeight"].toInt();

	} catch (...) {}
	return s;
}
void AppSettings::save() const {
	QFile f(settingsPath());
	if (!f.open(QIODevice::WriteOnly|QIODevice::Truncate))
		return;

	QJsonArray browsers;
	for (auto b: extraBrowsers) {
		browsers.append(QJsonObject{
			{"id", b.id},
			{"name", b.name},
			{"windowsRegistryPath", b.windowsRegistryPath},
			{"linuxPolicyRoot", b.linuxPolicyRoot},
			{"macBundleId", b.macBundleId},
			{"executable", b.executable},
			{"policyUrl", b.policyUrl}
		});
	}

	QJsonObject o{
		{"language",language},
		{"browserId",browserId},
		{"browsers",browsers},
		{"policyScope",policyScope},
		{"windowWidth",windowWidth},
		{"windowHeight",windowHeight}
	};
	f.write(QJsonDocument(o).toJson(QJsonDocument::Indented));
}

QList<LangPair> allLangs() {
	QList<LangPair> result({
		{u"en", u"English"},
		{u"ru", u"Русский"},
		{u"fr", u"Français"},
		{u"es", u"Español"},
		{u"de", u"Deutsch"},
	});

	return result;
}
