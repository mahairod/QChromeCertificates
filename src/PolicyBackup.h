#pragma once
#include "PolicyModel.h"
#include <QString>
namespace PolicyBackup {
void save(const QString &path,const PolicyState &state);
PolicyState load(const QString &path);
}
