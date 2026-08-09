#pragma once
#include <QDialog>
#include <QStringList>
class QPlainTextEdit; class QCheckBox;
class ConstraintsDialog : public QDialog {
	Q_OBJECT
public:
	static bool edit(QWidget *parent,const QString&certificateName,const QString&issuer,const QStringList&current,QStringList&result);
	static QStringList parseDomains(const QString&text);
	static QStringList expandSubdomains(const QStringList&domains);
	static QStringList collapseSubdomainPairs(const QStringList&domains);
	static QStringList parseCidrs(const QString&text);
private:
	ConstraintsDialog(QWidget*,const QString&,const QString&,const QStringList&);
	void validate();
	QPlainTextEdit *domains_; QCheckBox *includeSubdomains_; QStringList result_;
};
