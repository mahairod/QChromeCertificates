#include "AppSettings.h"
#include "BrowserDefinition.h"
#include <QCoreApplication>
#include <QFile>
#include <QJsonDocument>
#include <QJsonObject>
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
		auto o = QJsonDocument::fromJson(f.readAll()).object();

		QString lang = o["language"].toString();
		if (LANG_CODE_PATT.match(lang).hasMatch())
			s.language = lang;

		if (!BrowserDefinition::find(o["browserId"].toString()).id.isEmpty())
			s.browserId=o["browserId"].toString();

		if (o["policyScope"].toString()=="managed" || o["policyScope"].toString()=="recommended")
			s.policyScope = o["policyScope"].toString();

		if (o["windowWidth"].toInt()>=850) s.windowWidth=o["windowWidth"].toInt();
		if (o["windowHeight"].toInt()>=520) s.windowHeight=o["windowHeight"].toInt();
	} catch (...) {}
	return s;
}
void AppSettings::save() const {
	QFile f(settingsPath());
	if (!f.open(QIODevice::WriteOnly|QIODevice::Truncate))
		return;
	QJsonObject o{
		{"language",language},
		{"browserId",browserId},
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
