#pragma once
#include <QString>
#include <QList>
#include <QJsonObject>
#include <QJsonArray>
#include <optional>

struct CertificatePolicyEntry {
    QString certificateBase64;
    QStringList dnsNames;
    QStringList cidrs;
};

struct PolicyState {
    std::optional<bool> platformIntegrationEnabled;
    QList<CertificatePolicyEntry> entries;
};

QJsonObject entryToJson(const CertificatePolicyEntry &entry);
CertificatePolicyEntry entryFromJson(const QJsonObject &obj);
QJsonArray entriesToJson(const QList<CertificatePolicyEntry> &entries);
QList<CertificatePolicyEntry> entriesFromJson(const QJsonArray &array);
QByteArray serializeEntries(const QList<CertificatePolicyEntry> &entries);
PolicyState policyStateFromJson(const QJsonObject &obj);
QJsonObject policyStateToJson(const PolicyState &state);
