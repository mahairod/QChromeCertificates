#include "ConstraintsDialog.h"

#include <QByteArray>
#include <QCheckBox>
#include <QHostAddress>
#include <QLabel>
#include <QMessageBox>
#include <QPlainTextEdit>
#include <QPushButton>
#include <QRegularExpression>
#include <QUrl>
#include <QVBoxLayout>
#include <stdexcept>

static QStringList splitValues(const QString& t) {
	return t.split(QRegularExpression("[\\r\\n,;]+"), Qt::SkipEmptyParts);
}
static QString idnAscii(const QString& s) {
	// Qt's IDNA conversion handles Unicode DNS names and is available in Qt 6.
	return QUrl::toAce(s);
}
QStringList ConstraintsDialog::parseDomains(const QString& text) {
	QStringList out;
	for (QString raw : splitValues(text)) {
		raw = raw.trimmed();
		if (raw.contains("://") || raw.contains("/") || raw.contains(":") || raw.contains("*"))
			throw std::runtime_error(("Invalid domain: " + raw).toStdString());
		bool dot = raw.startsWith('.');
		QString v = dot ? raw.mid(1) : raw;
		v = v.endsWith('.') ? v.chopped(1) : v;
		QString a = idnAscii(v).toLower();
		auto labels = a.split('.');
		if (a.isEmpty() || a.size() > 253) throw std::runtime_error(("Invalid domain: " + raw).toStdString());
		for (auto& l : labels)
			if (l.isEmpty() || l.size() > 63 || l.startsWith('-') || l.endsWith('-') ||
				!QRegularExpression("^[a-z0-9-]+$").match(l).hasMatch())
				throw std::runtime_error(("Invalid domain: " + raw).toStdString());
		QString n = (dot ? "." : "") + a;
		if (!out.contains(n, Qt::CaseInsensitive)) out << n;
	}
	return out;
}
QStringList ConstraintsDialog::expandSubdomains(const QStringList& domains) {
	QStringList out;
	for (auto& d : domains) {
		if (!out.contains(d, Qt::CaseInsensitive)) out << d;
		if (!d.startsWith('.') && !out.contains("." + d, Qt::CaseInsensitive)) out << "." + d;
	}
	return out;
}
QStringList ConstraintsDialog::collapseSubdomainPairs(const QStringList& domains) {
	QStringList out;
	for (auto& d : domains)
		if (!(d.startsWith('.') && domains.contains(d.mid(1), Qt::CaseInsensitive))) out << d;
	return out;
}
QStringList ConstraintsDialog::parseCidrs(const QString& text) {
	QStringList out;
	for (auto& raw : splitValues(text)) {
		auto p = raw.split('/');
		QHostAddress a;
		if (p.size() != 2 || raw.contains('%') || !a.setAddress(p[0]))
			throw std::runtime_error(("Invalid CIDR network: " + raw).toStdString());
		bool ok = false;
		int prefix = p[1].toInt(&ok);
		int max = a.protocol() == QAbstractSocket::IPv4Protocol ? 32 : 128;
		if (!ok || prefix < 0 || prefix > max)
			throw std::runtime_error(("Invalid CIDR prefix length: " + raw).toStdString());
		quint32 v = 0;
		if (a.protocol() == QAbstractSocket::IPv4Protocol) {
			v = a.toIPv4Address();
			quint32 mask = prefix == 0 ? 0 : 0xFFFFFFFFu << (32 - prefix);
			v &= mask;
			QString n = QHostAddress(v).toString() + "/" + QString::number(prefix);
			if (!out.contains(n, Qt::CaseInsensitive)) out << n;
		} else {
			Q_IPV6ADDR v6 = a.toIPv6Address();
			int full = prefix / 8, rem = prefix % 8;
			if (rem) v6.c[full] &= (0xFF << (8 - rem)), full++;
			for (int i = full; i < 16; i++) v6.c[i] = 0;
			QString n = QHostAddress(v6).toString() + "/" + QString::number(prefix);
			if (!out.contains(n, Qt::CaseInsensitive)) out << n;
		}
	}
	return out;
}
ConstraintsDialog::ConstraintsDialog(QWidget* p, const QString& cert, const QString& issuer, const QStringList& current)
	: QDialog(p) {
	setWindowTitle("Allowed domains / Разрешённые домены");
	resize(640, 440);
	auto* l = new QVBoxLayout(this);
	l->addWidget(new QLabel(cert + "\nIssuer: " + issuer));
	domains_ = new QPlainTextEdit(this);
	domains_->setPlainText(current.join("\n"));
	l->addWidget(domains_);
	includeSubdomains_ = new QCheckBox("Automatically include subdomains / Автоматически включать поддомены", this);
	includeSubdomains_->setChecked(true);
	l->addWidget(includeSubdomains_);
	auto* ok = new QPushButton("OK", this);
	auto* cancel = new QPushButton("Cancel", this);
	auto* row = new QHBoxLayout;
	row->addStretch();
	row->addWidget(ok);
	row->addWidget(cancel);
	l->addLayout(row);
	connect(ok, &QPushButton::clicked, this, &ConstraintsDialog::validate);
	connect(cancel, &QPushButton::clicked, this, &QDialog::reject);
}
void ConstraintsDialog::validate() {
	try {
		result_ = parseDomains(domains_->toPlainText());
		if (includeSubdomains_->isChecked()) result_ = expandSubdomains(result_);
		if (result_.isEmpty()) throw std::runtime_error("Enter at least one domain.");
		accept();
	} catch (const std::exception& e) {
		QMessageBox::warning(this, "Invalid domain", e.what());
	}
}
bool ConstraintsDialog::edit(QWidget* p, const QString& cert, const QString& issuer, const QStringList& cur,
							 QStringList& res) {
	ConstraintsDialog d(p, cert, issuer, collapseSubdomainPairs(cur));
	if (d.exec() != QDialog::Accepted) return false;
	res = d.result_;
	return true;
}
