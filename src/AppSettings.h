#pragma once
#include <QString>

struct AppSettings {
    QString language;
    QString browserId = "chrome";
    QString policyScope = "managed";
    int windowWidth = 980;
    int windowHeight = 640;

    static AppSettings load();
    void save() const;
};
