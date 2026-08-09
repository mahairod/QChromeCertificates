#pragma once
#include "PolicyStore.h"
class WindowsPolicyStore : public PolicyStore {
public:
    WindowsPolicyStore(const BrowserDefinition &browser, const QString &testPath = {});
    PolicyState load() override;
    void save(const PolicyState &state) override;
    QString statusText() const override;
    bool machinePolicyPresent() const override;
private:
    BrowserDefinition browser_; QString path_;
};
QString buildRegistryFile(const QString &sid,const QString &registryPath,const PolicyState &state);
bool saveRegistryElevated(const QString &registryPath,const PolicyState &state);
