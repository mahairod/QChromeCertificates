#pragma once
#include "PolicyModel.h"
#include "BrowserDefinition.h"
#include <QString>
#include <memory>

enum class PolicyScope { Managed, Recommended };

class PolicyStore {
public:
	virtual ~PolicyStore() = default;
	virtual PolicyState load() = 0;
	virtual void save(const PolicyState &state) = 0;
	virtual bool machinePolicyPresent() const { return false; }
	virtual QString statusText() const = 0;
	virtual bool needsElevation() const { return false; }

	static std::unique_ptr<PolicyStore> create(const BrowserDefinition &browser, PolicyScope scope, const QString &testPath = {});
};
