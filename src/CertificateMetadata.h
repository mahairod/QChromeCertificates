#pragma once
#include <QString>
#include <QByteArray>

struct CertificateMetadata {
    QString commonName;
    QString subject;
    QString issuer;
    QString issuerOrganization;
    QString sha1;
    QString notAfter;
    bool isCA = false;
    QByteArray der;
};

CertificateMetadata readCertificate(const QByteArray &der);
CertificateMetadata readCertificateFile(const QString &path);
