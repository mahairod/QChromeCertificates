#include "PolicyModel.h"
#include <QJsonDocument>
#include <stdexcept>

static QStringList stringArray(const QJsonValue &v) {
	if (v.isUndefined() || v.isNull()) return {};
	if (!v.isArray()) throw std::runtime_error("Expected JSON array");
	QStringList out;
	for (const auto &x : v.toArray()) {
		if (!x.isString()) throw std::runtime_error("Expected JSON string");
		out << x.toString();
	}
	return out;
}

QJsonObject entryToJson(const CertificatePolicyEntry &entry) {
	QJsonObject constraints;
	if (!entry.dnsNames.isEmpty()) {
		QJsonArray a; for (const auto &s : entry.dnsNames) a.append(s);
		constraints["permitted_dns_names"] = a;
	}
	if (!entry.cidrs.isEmpty()) {
		QJsonArray a; for (const auto &s : entry.cidrs) a.append(s);
		constraints["permitted_cidrs"] = a;
	}
	QJsonObject o;
	o["certificate"] = entry.certificateBase64;
	o["constraints"] = constraints;
	return o;
}

CertificatePolicyEntry entryFromJson(const QJsonObject &obj) {
	if (!obj.contains("certificate") || !obj["certificate"].isString())
		throw std::runtime_error("A policy entry does not contain certificate.");
	CertificatePolicyEntry e;
	e.certificateBase64 = obj["certificate"].toString();
	if (obj.contains("constraints")) {
		if (!obj["constraints"].isObject()) throw std::runtime_error("The constraints field has an invalid format.");
		const auto c = obj["constraints"].toObject();
		e.dnsNames = stringArray(c["permitted_dns_names"]);
		e.cidrs = stringArray(c["permitted_cidrs"]);
	}
	return e;
}

QJsonArray entriesToJson(const QList<CertificatePolicyEntry> &entries) {
	QJsonArray a;
	for (const auto &e : entries) a.append(entryToJson(e));
	return a;
}

QList<CertificatePolicyEntry> entriesFromJson(const QJsonArray &array) {
	QList<CertificatePolicyEntry> out;
	for (const auto &v : array) {
		if (!v.isObject()) throw std::runtime_error("A policy entry is not an object.");
		out << entryFromJson(v.toObject());
	}
	return out;
}

QByteArray serializeEntries(const QList<CertificatePolicyEntry> &entries) {
	return QJsonDocument(entriesToJson(entries)).toJson(QJsonDocument::Compact);
}

PolicyState policyStateFromJson(const QJsonObject &obj) {
	PolicyState s;
	if (obj.contains("platformIntegrationEnabled") && !obj["platformIntegrationEnabled"].isNull())
		s.platformIntegrationEnabled = obj["platformIntegrationEnabled"].toBool();
	if (obj.contains("entries")) {
		if (!obj["entries"].isArray()) throw std::runtime_error("entries must be an array");
		s.entries = entriesFromJson(obj["entries"].toArray());
	}
	return s;
}

QJsonObject policyStateToJson(const PolicyState &state) {
	QJsonObject o;
	if (state.platformIntegrationEnabled.has_value())
		o["platformIntegrationEnabled"] = *state.platformIntegrationEnabled;
	else
		o["platformIntegrationEnabled"] = QJsonValue(QJsonValue::Null);
	o["entries"] = entriesToJson(state.entries);
	return o;
}
