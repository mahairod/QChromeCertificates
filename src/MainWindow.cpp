#include "MainWindow.h"
#include "CertificateMetadata.h"
#include "ConstraintsDialog.h"
#include "PolicyBackup.h"
#include "WindowsPolicyStore.h"
#include <QComboBox>
#include <QLabel>
#include <QCheckBox>
#include <QTableWidget>
#include <QHeaderView>
#include <QPushButton>
#include <QHBoxLayout>
#include <QVBoxLayout>
#include <QFileDialog>
#include <QMessageBox>
#include <QCloseEvent>
#include <QClipboard>
#include <QApplication>
#include <QDateTime>

MainWindow::MainWindow(AppSettings s,const QString&t):settings_(s),testPath_(t),browser_(BrowserDefinition::find(s.browserId)){setupUi();reload();}
QString MainWindow::tr(const QString&ru,const QString&en)const{return settings_.language=="ru"?ru:en;}
void MainWindow::setupUi(){
	resize(settings_.windowWidth,settings_.windowHeight);setMinimumSize(850,520);
	auto *central=new QWidget(this);setCentralWidget(central);auto *v=new QVBoxLayout(central);
	auto *top=new QHBoxLayout;top->addWidget(new QLabel(tr("Браузер:","Browser:")));browserBox_=new QComboBox;for(auto b:BrowserDefinition::all())browserBox_->addItem(b.name,b.id);browserBox_->setCurrentText(browser_.name);top->addWidget(browserBox_,1);
	top->addSpacing(15);top->addWidget(new QLabel(tr("Язык:","Language:")));languageBox_=new QComboBox;languageBox_->addItems({"Русский","English"});languageBox_->setCurrentIndex(settings_.language=="ru"?0:1);top->addWidget(languageBox_);
	top->addSpacing(15);top->addWidget(new QLabel(tr("Политика:","Policy:")));scopeBox_=new QComboBox;scopeBox_->addItems({"Managed","Recommended"});
	scopeBox_->setCurrentIndex(settings_.policyScope=="managed"?0:1);
#ifdef Q_OS_WIN
	scopeBox_->setEnabled(false);
#endif
	top->addWidget(scopeBox_);v->addLayout(top);
	description_=new QLabel;description_->setWordWrap(true);v->addWidget(description_);
	isolation_=new QCheckBox;isolation_->setStyleSheet("font-weight:bold");v->addWidget(isolation_);
	table_=new QTableWidget(0,4);table_->setSelectionBehavior(QAbstractItemView::SelectRows);table_->setSelectionMode(QAbstractItemView::SingleSelection);table_->setEditTriggers(QAbstractItemView::NoEditTriggers);
	table_->setHorizontalHeaderLabels({"Certificate","SHA-1","Allowed domains","Valid until"});table_->horizontalHeader()->setStretchLastSection(true);table_->setColumnWidth(0,230);table_->setColumnWidth(1,270);v->addWidget(table_,1);
	auto *buttons=new QHBoxLayout;auto *add=new QPushButton;remove_=new QPushButton;auto *imp=new QPushButton;auto *exp=new QPushButton;add->setText(tr("Добавить сертификат…","Add cert…"));remove_->setText(tr("Удалить сертификат","Remove cert"));imp->setText(tr("Импорт…","Import…"));exp->setText(tr("Экспорт…","Export…"));buttons->addWidget(add);buttons->addWidget(remove_);buttons->addWidget(imp);buttons->addWidget(exp);buttons->addStretch();v->addLayout(buttons);
	auto *foot=new QHBoxLayout;status_=new QLabel;apply_=new QPushButton(tr("Применить","Apply"));cancel_=new QPushButton(tr("Отмена","Cancel"));foot->addWidget(status_,1);foot->addWidget(apply_);foot->addWidget(cancel_);v->addLayout(foot);
	connect(browserBox_,&QComboBox::currentIndexChanged,this,&MainWindow::browserChanged);connect(languageBox_,&QComboBox::currentIndexChanged,this,&MainWindow::languageChanged);connect(scopeBox_,&QComboBox::currentIndexChanged,this,&MainWindow::scopeChanged);
	connect(isolation_,&QCheckBox::toggled,this,&MainWindow::isolationChanged);connect(add,&QPushButton::clicked,this,&MainWindow::addCertificate);connect(remove_,&QPushButton::clicked,this,&MainWindow::removeCertificate);connect(imp,&QPushButton::clicked,this,&MainWindow::importPolicies);connect(exp,&QPushButton::clicked,this,&MainWindow::exportPolicies);connect(apply_,&QPushButton::clicked,this,&MainWindow::applyPolicies);connect(cancel_,&QPushButton::clicked,this,&MainWindow::cancelPolicies);connect(table_,&QTableWidget::cellDoubleClicked,this,[this](int,int){editDomains();});
	description_->setText(tr("Политики применяются к текущему пользователю. Встроенное хранилище корневых сертификатов браузера остаётся включённым.","Policies apply to the current user. The browser's built-in root certificate store remains enabled."));
	isolation_->setText(tr("Изолировать браузерное хранилище сертификатов","Isolate the browser certificate store"));
}
void MainWindow::reload(){
	try{
		PolicyScope scope=settings_.policyScope=="managed"?PolicyScope::Managed:PolicyScope::Recommended;
		store_=PolicyStore::create(browser_,scope,testPath_);state_=store_->load();machinePolicies_=store_->machinePolicyPresent();
		updating_=true;isolation_->setChecked(state_.platformIntegrationEnabled.has_value()&&!*state_.platformIntegrationEnabled);if(!state_.entries.isEmpty())isolation_->setChecked(true);updating_=false;
		refreshTable();baseline_=signature();updateButtons();
	}catch(const std::exception&e){updating_=false;QMessageBox::critical(this,tr("Ошибка","Error"),tr("Не удалось прочитать политики:\n\n","Could not read policies:\n\n")+e.what());}
}
void MainWindow::refreshTable(){
	table_->setRowCount(0);for(const auto&e:state_.entries){try{auto m=readCertificate(QByteArray::fromBase64(e.certificateBase64.toLatin1()));int r=table_->rowCount();table_->insertRow(r);table_->setItem(r,0,new QTableWidgetItem(m.commonName.isEmpty()?m.subject:m.commonName));table_->setItem(r,1,new QTableWidgetItem(m.sha1));table_->setItem(r,2,new QTableWidgetItem(e.dnsNames.join(", ")));table_->setItem(r,3,new QTableWidgetItem(m.notAfter));}catch(...){int r=table_->rowCount();table_->insertRow(r);table_->setItem(r,0,new QTableWidgetItem("Invalid certificate"));}}
	status_->setText(store_?store_->statusText():"");updateButtons();
}
QString MainWindow::signature()const{return QString(isolation_->isChecked()?"1|":"0|")+QString::fromUtf8(serializeEntries(state_.entries));}
bool MainWindow::dirty()const{return !baseline_.isNull()&&baseline_!=signature();}
void MainWindow::updateButtons(){bool d=dirty();apply_->setEnabled(d);cancel_->setEnabled(d);browserBox_->setEnabled(!d&&!testPath_.size());scopeBox_->setEnabled(!d);remove_->setEnabled(isolation_->isChecked()&&table_->currentRow()>=0);setWindowTitle("QChromeCertificates 3.0"+QString(d?" *":""));status_->setStyleSheet((machinePolicies_?"color:#b36b00;":""));}
void MainWindow::browserChanged(int i){if(updating_||i<0)return;if(dirty()){QMessageBox::warning(this,tr("Есть изменения","Unapplied changes"),tr("Сначала примените или отмените изменения.","Apply or cancel the changes first."));updating_=true;browserBox_->setCurrentText(browser_.name);updating_=false;return;}browser_=BrowserDefinition::find(browserBox_->currentData().toString());settings_.browserId=browser_.id;settings_.save();reload();}
void MainWindow::languageChanged(int i){if(updating_)return;settings_.language=i==0?"ru":"en";settings_.save();setupUi();}
void MainWindow::scopeChanged(int i){if(updating_)return;settings_.policyScope=i==0?"managed":"recommended";settings_.save();reload();}
void MainWindow::isolationChanged(bool on){if(updating_)return;if(!on&&!state_.entries.isEmpty()){if(QMessageBox::question(this,tr("Удаление сертификатов","Remove certificates"),tr("Отключение изоляции удалит все добавленные сертификаты при применении изменений. Продолжить?","Turning off isolation will remove all added certificates when changes are applied. Continue?"))!=QMessageBox::Yes){updating_=true;isolation_->setChecked(true);updating_=false;return;}state_.entries.clear();refreshTable();}updateButtons();}
void MainWindow::addCertificate(){
	QString p=QFileDialog::getOpenFileName(this,tr("Выберите корневой сертификат","Select a root certificate"),{},tr("Certificates (*.cer *.crt *.der);;All files (*)","Certificates (*.cer *.crt *.der);;All files (*)"));if(p.isEmpty())return;
	try{auto m=readCertificateFile(p);if(!m.isCA)throw std::runtime_error("The selected certificate is not a CA certificate.");for(const auto&e:state_.entries)if(readCertificate(QByteArray::fromBase64(e.certificateBase64.toLatin1())).sha1.compare(m.sha1,Qt::CaseInsensitive)==0){QMessageBox::information(this,"Certificate",tr("Этот сертификат уже добавлен.","This certificate has already been added."));return;}CertificatePolicyEntry e;e.certificateBase64=m.der.toBase64();state_.entries.append(e);isolation_->setChecked(true);refreshTable();}
	catch(const std::exception&e){QMessageBox::critical(this,tr("Ошибка","Error"),tr("Не удалось добавить сертификат:\n\n","Could not add the certificate:\n\n")+e.what());}
}
void MainWindow::removeCertificate(){int r=table_->currentRow();if(r<0){QMessageBox::information(this,"Certificate",tr("Выберите сертификат в списке.","Select a certificate in the list."));return;}auto m=readCertificate(QByteArray::fromBase64(state_.entries[r].certificateBase64.toLatin1()));if(QMessageBox::question(this,"Confirmation",tr("Удалить сертификат «","Remove certificate “")+m.commonName+tr("»?","”?"))==QMessageBox::Yes){state_.entries.removeAt(r);refreshTable();}}
void MainWindow::editDomains(){int r=table_->currentRow();if(r<0)return;if(!state_.entries[r].cidrs.isEmpty()){QMessageBox::warning(this,"CIDR",tr("Эта запись содержит CIDR-ограничения из версии 1. Их нельзя редактировать в версии 2.","This entry contains legacy CIDR constraints and cannot be edited in version 2."));return;}auto m=readCertificate(QByteArray::fromBase64(state_.entries[r].certificateBase64.toLatin1()));QStringList d;if(ConstraintsDialog::edit(this,m.commonName,m.issuerOrganization.isEmpty()?m.issuer:m.issuerOrganization,state_.entries[r].dnsNames,d)){state_.entries[r].dnsNames=d;refreshTable();}}
PolicyState MainWindow::buildSaveState()const{PolicyState s;if(isolation_->isChecked()){s.platformIntegrationEnabled=false;s.entries=state_.entries;}return s;}
void MainWindow::validateState(const PolicyState&s){for(const auto&e:s.entries){if(e.certificateBase64.isEmpty())throw std::runtime_error("The backup contains an empty certificate.");auto m=readCertificate(QByteArray::fromBase64(e.certificateBase64.toLatin1()));if(!m.isCA)throw std::runtime_error("The selected certificate is not a CA certificate.");ConstraintsDialog::parseDomains(e.dnsNames.join("\n"));ConstraintsDialog::parseCidrs(e.cidrs.join("\n"));}}
void MainWindow::applyPolicies(){
	try{if(isolation_->isChecked())for(const auto&e:state_.entries){if(!e.cidrs.isEmpty())throw std::runtime_error(tr("CIDR-ограничения из версии 1 нельзя применить в версии 2.","Legacy CIDR constraints cannot be applied in version 2.").toStdString());if(e.dnsNames.isEmpty())throw std::runtime_error(tr("Для каждого сертификата требуется хотя бы один домен.","Each certificate requires at least one domain.").toStdString());}
		auto s=buildSaveState();
		try {
			store_->save(s);
		} catch(const std::exception&) {
#ifdef Q_OS_WIN
			if(!saveRegistryElevated(browser_.windowsRegistryPath,s)) return;
#else
			throw;
#endif
		}
		reload();QMessageBox::information(this,tr("Изменения применены","Changes applied"),tr("Изменения применены. Откройте страницу %1 для проверки.").arg(browser_.policyUrl));QApplication::clipboard()->setText(browser_.policyUrl);
	}catch(const std::exception&e){QMessageBox::critical(this,tr("Ошибка","Error"),tr("Не удалось применить политики:\n\n","Could not apply policies:\n\n")+e.what());}
}
void MainWindow::cancelPolicies(){reload();}
void MainWindow::exportPolicies(){QString p=QFileDialog::getSaveFileName(this,tr("Сохранить резервную копию","Save policy backup"),browser_.name+"CertificatePolicy-"+QDateTime::currentDateTime().toString("yyyyMMdd-HHmmss")+".json","Policy backups (*.json)");if(!p.isEmpty())try{PolicyBackup::save(p,buildSaveState());}catch(const std::exception&e){QMessageBox::critical(this,"Error",e.what());}}
void MainWindow::importPolicies(){QString p=QFileDialog::getOpenFileName(this,tr("Выберите резервную копию","Select a policy backup"),{},"Policy backups (*.json);;All files (*)");if(p.isEmpty())return;try{auto s=PolicyBackup::load(p);validateState(s);updating_=true;state_=s;isolation_->setChecked(s.platformIntegrationEnabled.has_value()&&! *s.platformIntegrationEnabled || !s.entries.isEmpty());updating_=false;refreshTable();}catch(const std::exception&e){updating_=false;QMessageBox::critical(this,"Error",e.what());}}
void MainWindow::closeEvent(QCloseEvent*e){if(dirty()&&QMessageBox::question(this,tr("Есть неприменённые изменения","Unapplied changes"),tr("Закрыть программу и отбросить неприменённые изменения?","Close and discard unapplied changes?"))!=QMessageBox::Yes){e->ignore();return;}settings_.windowWidth=width();settings_.windowHeight=height();settings_.save();e->accept();}
