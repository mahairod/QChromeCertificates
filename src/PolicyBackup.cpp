#include "PolicyBackup.h"
#include <QFile>
#include <QJsonDocument>
#include <QJsonObject>
#include <QDateTime>
#include <stdexcept>

namespace PolicyBackup {

void save(const QString&p,const PolicyState&s){
	QJsonObject o{
		{"formatVersion",1},
		{"createdAtUtc",QDateTime::currentDateTimeUtc().toString(Qt::ISODateWithMs)},
		{"policy",policyStateToJson(s)}
	};
	QFile f(p);
	if(!f.open(QIODevice::WriteOnly))
		throw std::runtime_error(("Cannot write "+p).toStdString());
	f.write(QJsonDocument(o).toJson(QJsonDocument::Indented));
}

PolicyState load(const QString&p){
	QFile f(p);
	if(!f.open(QIODevice::ReadOnly))
		throw std::runtime_error(("Cannot read "+p).toStdString());
	auto o=QJsonDocument::fromJson(f.readAll()).object();
	if(o["formatVersion"].toInt()!=1||!o["policy"].isObject())
		throw std::runtime_error("Unsupported policy backup format.");
	return policyStateFromJson(o["policy"].toObject());
}

}
