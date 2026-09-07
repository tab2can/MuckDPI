#include <windows.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "muckdpi.h"
#include "service.h"

#define SERVICE_NAME "MuckDPI"

static SERVICE_STATUS ServiceStatus;
static SERVICE_STATUS_HANDLE hStatus;
static int service_argc = 0;
static char **service_argv = NULL;

int service_register(int argc, char *argv[])
{
    int i, ret;
    SERVICE_TABLE_ENTRY ServiceTable[] = {
        {SERVICE_NAME, (LPSERVICE_MAIN_FUNCTION)service_main},
        {NULL, NULL}
    };
    /*
     * Save argc & argv as service_main is called with different
     * arguments, which are passed from "start" command, not
     * from the program command line.
     * We don't need this behaviour.
     *
     * Note that if StartServiceCtrlDispatcher() succeedes
     * it does not return until the service is stopped,
     * so we should copy all arguments first and then
     * handle the failure.
     */
    if (!service_argc && !service_argv) {
        service_argc = argc;
        service_argv = calloc((size_t)(argc + 1), sizeof(void*));
        for (i = 0; i < argc; i++) {
            service_argv[i] = strdup(argv[i]);
        }
    }

    ret = StartServiceCtrlDispatcher(ServiceTable);

    if (service_argc && service_argv) {
        for (i = 0; i < service_argc; i++) {
            free(service_argv[i]);
        }
        free(service_argv);
    }

    return ret;
}

void service_main(int argc __attribute__((unused)),
                  char *argv[] __attribute__((unused)))
{
    ServiceStatus.dwServiceType = SERVICE_WIN32_OWN_PROCESS; 
    ServiceStatus.dwCurrentState = SERVICE_RUNNING;
    ServiceStatus.dwControlsAccepted = SERVICE_ACCEPT_STOP | SERVICE_ACCEPT_SHUTDOWN;
    ServiceStatus.dwWin32ExitCode = 0;
    ServiceStatus.dwServiceSpecificExitCode = 0;
    ServiceStatus.dwCheckPoint = 1;
    ServiceStatus.dwWaitHint = 0;

    hStatus = RegisterServiceCtrlHandler(
        SERVICE_NAME,
        (LPHANDLER_FUNCTION)service_controlhandler);
    if (hStatus == (SERVICE_STATUS_HANDLE)0)
    {
        // Registering Control Handler failed
        return;
    }

    SetServiceStatus(hStatus, &ServiceStatus);

    // Calling main with saved argc & argv
    ServiceStatus.dwWin32ExitCode = (DWORD)main(service_argc, service_argv);
    ServiceStatus.dwCurrentState  = SERVICE_STOPPED;
    SetServiceStatus(hStatus, &ServiceStatus);
    return;
}

static int is_sc_noise(const char *a)
{
    if (!a || !*a)
        return 1;
    if (a[0] == '-' )
        return 0;
    /* Leftover `sc create ... start= auto` tokens from launch arguments. */
    if (!_stricmp(a, "auto") || !_stricmp(a, "start") || !_strnicmp(a, "start=", 6))
        return 1;
    return 0;
}

static int append_quoted(char *out, size_t *used, size_t cap, const char *s)
{
    size_t i = *used;
    size_t need = 3; /* quotes + space */
    const char *p;
    for (p = s; *p; p++)
        need += (*p == '"') ? 2 : 1;
    if (i + need >= cap)
        return 0;
    if (i)
        out[i++] = ' ';
    out[i++] = '"';
    for (p = s; *p; p++) {
        if (*p == '"')
            out[i++] = '"';
        out[i++] = *p;
    }
    out[i++] = '"';
    out[i] = '\0';
    *used = i;
    return 1;
}

void service_install_autostart(int argc, char *argv[])
{
    char exe[MAX_PATH];
    char binpath[32768];
    size_t used = 0;
    int i;
    SC_HANDLE scm, svc;
    SERVICE_DESCRIPTIONA desc;
    DWORD err;

    if (GetModuleFileNameA(NULL, exe, MAX_PATH) == 0)
        return;
    if (!append_quoted(binpath, &used, sizeof(binpath), exe))
        return;
    for (i = 1; i < argc; i++) {
        if (is_sc_noise(argv[i]))
            continue;
        if (!append_quoted(binpath, &used, sizeof(binpath), argv[i]))
            return;
    }

    scm = OpenSCManagerA(NULL, NULL, SC_MANAGER_CONNECT | SC_MANAGER_CREATE_SERVICE);
    if (!scm) {
        puts("Could not open the service manager (need administrator) — Windows start=auto was not set.");
        return;
    }

    svc = CreateServiceA(
        scm, SERVICE_NAME, SERVICE_NAME,
        SERVICE_ALL_ACCESS,
        SERVICE_WIN32_OWN_PROCESS,
        SERVICE_AUTO_START,
        SERVICE_ERROR_NORMAL,
        binpath, NULL, NULL, NULL, NULL, NULL);
    if (!svc) {
        err = GetLastError();
        if (err != ERROR_SERVICE_EXISTS) {
            printf("Could not create the MuckDPI service (error %lu).\n", err);
            CloseServiceHandle(scm);
            return;
        }
        svc = OpenServiceA(scm, SERVICE_NAME, SERVICE_ALL_ACCESS);
        if (!svc) {
            puts("Could not open the existing MuckDPI service.");
            CloseServiceHandle(scm);
            return;
        }
        if (!ChangeServiceConfigA(
                svc, SERVICE_WIN32_OWN_PROCESS, SERVICE_AUTO_START,
                SERVICE_ERROR_NORMAL, binpath, NULL, NULL, NULL, NULL, NULL, NULL)) {
            printf("Could not update the MuckDPI service (error %lu).\n", GetLastError());
            CloseServiceHandle(svc);
            CloseServiceHandle(scm);
            return;
        }
    }

    desc.lpDescription = "MuckDPI — DPI circumvention. Starts automatically with Windows.";
    ChangeServiceConfig2A(svc, SERVICE_CONFIG_DESCRIPTION, &desc);
    CloseServiceHandle(svc);
    CloseServiceHandle(scm);
    puts("Windows service MuckDPI is installed with start=auto (no console).");
}

// Control handler function
void service_controlhandler(DWORD request)
{
    switch(request)
    {
        case SERVICE_CONTROL_STOP:
        case SERVICE_CONTROL_SHUTDOWN:
            deinit_all();
            ServiceStatus.dwWin32ExitCode = 0;
            ServiceStatus.dwCurrentState  = SERVICE_STOPPED;
        default:
            break;
    }
    // Report current status
    SetServiceStatus(hStatus, &ServiceStatus);
    return;
}
