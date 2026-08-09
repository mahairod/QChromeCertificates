#pragma once
#include "PolicyStore.h"
class LinuxPolicyStore : public PolicyStore {
public:
    LinuxPolicyStore(const BrowserDefinition &browser, PolicyScope scope, const QString &rootOverride = {});
    PolicyState load() override;
    void save(const PolicyState &state) override;
    QString statusText() const override;
    bool needsElevation() const override;
private:
    BrowserDefinition browser_;
    PolicyScope scope_;
    QString root_;
    QString filePath() const;
};
