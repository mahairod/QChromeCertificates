#pragma once
#include <QMainWindow>
#include "AppSettings.h"
#include "BrowserDefinition.h"
#include "PolicyStore.h"
#include "PolicyModel.h"

class QComboBox;
class QLabel;
class QCheckBox;
class QTableWidget;
class QPushButton;

class MainWindow : public QMainWindow {
	Q_OBJECT
  public:
	MainWindow(AppSettings settings,const QString& testPath= {});
  protected:
	void closeEvent(QCloseEvent*) override;
  private slots:
	void browserChanged(int);
	void languageChanged(int);
	void scopeChanged(int);
	void isolationChanged(bool);
	void addCertificate();
	void removeCertificate();
	void editDomains();
	void importPolicies();
	void exportPolicies();
	void applyPolicies();
	void cancelPolicies();
  private:
	AppSettings settings_;
	QString testPath_;
	BrowserDefinition browser_;
	std::unique_ptr<PolicyStore> store_;
	PolicyState state_;
	QString baseline_;
	bool updating_=false;
	bool machinePolicies_=false;
	QComboBox* browserBox_, *languageBox_, *scopeBox_;
	QLabel* description_, *status_;
	QCheckBox* isolation_;
	QTableWidget* table_;
	QPushButton* remove_,*apply_,*cancel_;

	void setupUi();
	void reload();
	void refreshTable();
	void updateButtons();
	bool dirty() const;
	QString signature() const;
	PolicyState buildSaveState() const;
	void validateState(const PolicyState&);
};
