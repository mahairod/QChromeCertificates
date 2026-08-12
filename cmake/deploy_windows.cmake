if(NOT DEFINED APP_EXE)
    message(FATAL_ERROR "APP_EXE is not defined")
endif()

if(NOT DEFINED INSTALL_PREFIX)
    message(FATAL_ERROR "INSTALL_PREFIX is not defined")
endif()

if(NOT DEFINED WINDEPLOYQT)
    message(FATAL_ERROR "WINDEPLOYQT is not defined")
endif()

set(EXE "${INSTALL_PREFIX}/${APP_EXE}")

message(STATUS "Deploying Qt with windeployqt")
message(STATUS "Executable: ${EXE}")
message(STATUS "windeployqt: ${WINDEPLOYQT}")

execute_process(
    COMMAND
        "${WINDEPLOYQT}"
        --release
        --no-translations
        "${EXE}"

    RESULT_VARIABLE result
)

if(NOT result EQUAL 0)
    message(FATAL_ERROR
        "windeployqt failed with exit code ${result}"
    )
endif()
