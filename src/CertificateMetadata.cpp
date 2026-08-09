#include "CertificateMetadata.h"

#include <openssl/err.h>
#include <openssl/evp.h>
#include <openssl/x509.h>
#include <openssl/x509v3.h>

#include <QDateTime>
#include <QFile>
#include <stdexcept>

static QString namePart(X509_NAME* name, int nid) {
	if (!name) return {};
	int idx = X509_NAME_get_index_by_NID(name, nid, -1);
	if (idx < 0) return {};
	X509_NAME_ENTRY* e = X509_NAME_get_entry(name, idx);
	ASN1_STRING* s = X509_NAME_ENTRY_get_data(e);
	unsigned char* utf8 = nullptr;
	int n = ASN1_STRING_to_UTF8(&utf8, s);
	QString r = QString::fromUtf8(reinterpret_cast<char*>(utf8), n);
	OPENSSL_free(utf8);
	return r;
}
static QString fullName(X509_NAME* name) {
	if (!name) return {};
	BIO* b = BIO_new(BIO_s_mem());
	X509_NAME_print_ex(b, name, 0, XN_FLAG_RFC2253);
	char* p = nullptr;
	long n = BIO_get_mem_data(b, &p);
	QString r = QString::fromUtf8(p, n);
	BIO_free(b);
	return r;
}
CertificateMetadata readCertificate(const QByteArray& der) {
	const unsigned char* p = reinterpret_cast<const unsigned char*>(der.constData());
	X509* x = d2i_X509(nullptr, &p, der.size());
	if (!x) throw std::runtime_error("Invalid X.509 certificate.");
	CertificateMetadata m;
	m.der = der;
	m.commonName = namePart(X509_get_subject_name(x), NID_commonName);
	m.subject = fullName(X509_get_subject_name(x));
	m.issuer = fullName(X509_get_issuer_name(x));
	m.issuerOrganization = namePart(X509_get_issuer_name(x), NID_organizationName);
	unsigned int mdlen = 0;
	unsigned char md[EVP_MAX_MD_SIZE];
	if (!X509_digest(x, EVP_sha1(), md, &mdlen)) {
		X509_free(x);
		throw std::runtime_error("Cannot calculate SHA-1.");
	}
	QStringList hex;
	for (unsigned i = 0; i < mdlen; i++) hex << QString("%1").arg(md[i], 2, 16, QChar('0'));
	m.sha1 = hex.join(":").toUpper();
	const ASN1_TIME* na = X509_get0_notAfter(x);
	struct tm t {};
	if (ASN1_TIME_to_tm(na, &t) == 1) {
		QDate d(t.tm_year + 1900, t.tm_mon + 1, t.tm_mday);
		m.notAfter = d.toString("yyyy-MM-dd");
	}
	BASIC_CONSTRAINTS* bc = (BASIC_CONSTRAINTS*) X509_get_ext_d2i(x, NID_basic_constraints, nullptr, nullptr);
	m.isCA = bc && bc->ca;
	BASIC_CONSTRAINTS_free(bc);
	X509_free(x);
	return m;
}
CertificateMetadata readCertificateFile(const QString& path) {
	QFile f(path);
	if (!f.open(QIODevice::ReadOnly)) throw std::runtime_error(("Cannot open certificate: " + path).toStdString());
	QByteArray data = f.readAll();
	const unsigned char* p = reinterpret_cast<const unsigned char*>(data.constData());
	X509* x = d2i_X509(nullptr, &p, data.size());
	if (!x) throw std::runtime_error("Only DER certificates are accepted.");
	unsigned char* out = nullptr;
	int n = i2d_X509(x, &out);
	QByteArray der(reinterpret_cast<char*>(out), n);
	OPENSSL_free(out);
	X509_free(x);
	return readCertificate(der);
}
