#pragma once
#include "PolicyStore.h"
class MacPolicyStore : public PolicyStore {
public:
    MacPolicyStore(const BrowserDefinition &browser, PolicyScope scope);
    PolicyState load() override;
    void save(const PolicyState &state) override;
    QString statusText() const override;
    bool needsElevation() const override;
private:
    BrowserDefinition browser_; PolicyScope scope_;
    QString filePath() const;
};
