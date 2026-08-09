#include "PolicyStore.h"
#include "LinuxPolicyStore.h"
#include "MacosPolicyStore.h"
#include "WindowsPolicyStore.h"
std::unique_ptr<PolicyStore> PolicyStore::create(const BrowserDefinition&b,PolicyScope scope,const QString&testPath){
#ifdef Q_OS_WIN
    Q_UNUSED(scope); return std::make_unique<WindowsPolicyStore>(b,testPath);
#elif defined(Q_OS_MAC)
    Q_UNUSED(testPath); return std::make_unique<MacPolicyStore>(b,scope);
#else
    Q_UNUSED(testPath); return std::make_unique<LinuxPolicyStore>(b,scope);
#endif
}
