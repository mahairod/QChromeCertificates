cmake_minimum_required(VERSION 3.17)
project(QChromeCertificates VERSION 3.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

set(CMAKE_AUTOMOC ON)
set(CMAKE_AUTOUIC ON)
set(CMAKE_AUTORCC ON)

set(QTMODULES Core Gui Widgets Network LinguistTools)

find_package(Qt6 COMPONENTS ${QTMODULES})
if(Qt6_FOUND)
    set(QT_VERSION_MAJOR 6)
else()
    find_package(Qt5 REQUIRED COMPONENTS ${QTMODULES})
    set(QT_VERSION_MAJOR 5)
endif()

#find_package(Qt6 REQUIRED COMPONENTS Core Gui Widgets Network LinguistTools)
find_package(OpenSSL REQUIRED COMPONENTS Crypto)

qt_add_resources(RES "icons.qrc")

set(MAIN_EXECUTABLE qchrocert)

add_executable(${MAIN_EXECUTABLE}
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
    ${RES}
)

set(QTNS Qt${QT_VERSION_MAJOR})
target_link_libraries(${MAIN_EXECUTABLE} PRIVATE
    ${QTNS}::Core ${QTNS}::Gui ${QTNS}::Widgets ${QTNS}::Network OpenSSL::Crypto
)

if(WIN32)
    target_link_libraries(${MAIN_EXECUTABLE} PRIVATE advapi32 shell32 ole32)
endif()

target_compile_definitions(${MAIN_EXECUTABLE} PRIVATE QCC_VERSION="3.0")

qt_add_translations(${MAIN_EXECUTABLE}
	TS_FILES
		translations/${MAIN_EXECUTABLE}_ru.ts
		translations/${MAIN_EXECUTABLE}_fr.ts
		translations/${MAIN_EXECUTABLE}_es.ts
		translations/${MAIN_EXECUTABLE}_de.ts
	RESOURCE_PREFIX "/i18n"
)

# ========= installation ============

include(GNUInstallDirs)

set(CMAKE_INSTALL_PREFIX package)

# Install the executable to "${CMAKE_INSTALL_PREFIX}/bin".
install(TARGETS ${MAIN_EXECUTABLE}
	BUNDLE  DESTINATION .
	RUNTIME DESTINATION ${CMAKE_INSTALL_BINDIR}
)

if (QT_VERSION VERSION_GREATER 6.5)

# Generate the deployment script for MyApp.
qt_generate_deploy_app_script(
    TARGET ${MAIN_EXECUTABLE} app-icon.png
    FILENAME_VARIABLE deploy_script
    NO_UNSUPPORTED_PLATFORM_ERROR
)

# Run the deployment script during installation (on "cmake --install").
install(SCRIPT ${deploy_script})

endif ()

file(INSTALL ${CMAKE_SOURCE_DIR}/app-icon.png DESTINATION ${CMAKE_BINARY_DIR})

set(CPACK_PACKAGE_NAME ${CMAKE_PROJECT_NAME})
set(CPACK_PACKAGE_DESCRIPTION_SUMMARY "Chrome Certificates Manager")
set(CPACK_PACKAGE_VENDOR "Elliptica")
set(CPACK_PACKAGE_DESCRIPTION_FILE ${CMAKE_SOURCE_DIR}/docs/DESCRIPTION.txt)
file(TO_NATIVE_PATH "${CMAKE_BINARY_DIR}/app-icon.png" CPACK_PACKAGE_ICON)
message(PACKAGE_ICON: ${CPACK_PACKAGE_ICON})
#set(CPACK_PACKAGE_ICON "${CMAKE_BINARY_DIR}/app-icon.png")

set(CPACK_PACKAGE_INSTALL_DIRECTORY ${CPACK_PACKAGE_NAME})
set(CPACK_VERBATIM_VARIABLES ON)
set(CPACK_PACKAGING_INSTALL_PREFIX "/opt/elliptica")
set(CPACK_PACKAGE_CONTACT "anton@elliptica.ru")
set(CPACK_RESOURCE_FILE_LICENSE ${CMAKE_SOURCE_DIR}/LICENSE.md)
set(CPACK_RESOURCE_FILE_README ${CMAKE_SOURCE_DIR}/README.md)

set(CPACK_PACKAGE_EXECUTABLES "${MAIN_EXECUTABLE};${CMAKE_PROJECT_NAME}")

set(CPACK_PACKAGE_FILE_NAME
    "${CPACK_PACKAGE_NAME}-${PROJECT_VERSION}-${CMAKE_SYSTEM_NAME}-${CMAKE_SYSTEM_PROCESSOR}"
)
set(CPACK_CREATE_DESKTOP_LINKS ${CPACK_PACKAGE_NAME})

set(CPACK_SOURCE_IGNORE_FILES "/build;/.git")

set(CPACK_PACKAGE_CHECKSUM SHA256)
set(CPACK_PACKAGE_DIRECTORY dist)

string(REPLACE "_" "-" PROCESSOR_ARCH ${CMAKE_SYSTEM_PROCESSOR})


# === Debian specifics ===
set(QTLIBS_DEBIAN libqt${QT_VERSION_MAJOR}widgets${QT_VERSION_MAJOR} libqt${QT_VERSION_MAJOR}network${QT_VERSION_MAJOR})
set(CPACK_DEBIAN_PACKAGE_DEPENDS ${QTLIBS_DEBIAN})
#set(CPACK_DEBIAN_PACKAGE_MAINTAINER "Package Maintainer <anton@elliptica.ru>")
set(CPACK_DEBIAN_PACKAGE_HOMEPAGE "https://github.com/mahairod/QChromeCertificates")
set(CPACK_DEBIAN_COMPRESSION_TYPE lzma)

# === Rpm specifics ===
set(QTLIBS_RPM "qt${QT_VERSION_MAJOR}-qtbase(${PROCESSOR_ARCH}), qt${QT_VERSION_MAJOR}-qtbase-gui(${PROCESSOR_ARCH})" )

set(CPACK_RPM_PACKAGE_LICENSE "CC BY-NC-SA 4.0")
set(CPACK_RPM_PACKAGE_GROUP security)
set(CPACK_RPM_COMPRESSION_TYPE lzma)
#set(CPACK_RPM_PACKAGE_AUTOREQ yes)
set(CPACK_RPM_PACKAGE_REQUIRES "${QTLIBS_RPM}")
string(TOLOWER ${CMAKE_PROJECT_NAME} CPACK_RPM_PACKAGE_PROVIDES)

include(CPack)
