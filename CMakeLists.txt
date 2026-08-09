cmake_minimum_required(VERSION 3.20)
project(QChromeCertificates VERSION 3.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

set(CMAKE_AUTOMOC ON)
set(CMAKE_AUTOUIC ON)
set(CMAKE_AUTORCC ON)

find_package(Qt6 REQUIRED COMPONENTS Core Gui Widgets Network LinguistTools)
find_package(OpenSSL REQUIRED COMPONENTS Crypto)

add_executable(qchrocert
    src/main.cpp
    src/AppSettings.h src/AppSettings.cpp
    src/BrowserDefinition.h src/BrowserDefinition.cpp
    src/PolicyModel.h src/PolicyModel.cpp
    src/CertificateMetadata.h src/CertificateMetadata.cpp
    src/PolicyStore.h src/PolicyStore.cpp
    src/LinuxPolicyStore.h src/LinuxPolicyStore.cpp
    src/MacPolicyStore.h src/MacPolicyStore.cpp
    src/WindowsPolicyStore.h src/WindowsPolicyStore.cpp
    src/PolicyBackup.h src/PolicyBackup.cpp
    src/ConstraintsDialog.h src/ConstraintsDialog.cpp
    src/MainWindow.h src/MainWindow.cpp
)

target_link_libraries(qchrocert PRIVATE
    Qt6::Core Qt6::Gui Qt6::Widgets Qt6::Network OpenSSL::Crypto
)

if(WIN32)
    target_link_libraries(qchrocert PRIVATE advapi32 shell32 ole32)
endif()

target_compile_definitions(qchrocert PRIVATE QCC_VERSION="3.0")

include(GNUInstallDirs)
install(TARGETS qchrocert RUNTIME DESTINATION ${CMAKE_INSTALL_BINDIR})

qt_add_translations(qchrocert
	TS_FILES translations/qchrocert_ru.ts
	RESOURCE_PREFIX "/i18n"
)
