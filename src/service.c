#include <windows.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "muckdpi.h"
#include "service.h"

#ifndef SERVICE_CONFIG_DELAYED_AUTO_START_INFO
#define SERVICE_CONFIG_DELAYED_AUTO_START_INFO 3
typedef struct _SERVICE_DELAYED_AUTO_START_INFO {
    BOOL fDelayedAutostart;
} SERVICE_DELAYED_AUTO_START_INFO, *LPSERVICE_DELAYED_AUTO_START_INFO;
#endif

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

enum start_kind {
    START_AUTOMATIC = 0,
    START_AUTOMATIC_DELAYED,
    START_MANUAL,
    START_DISABLED
};

static int is_sc_noise(const char *a)
{
    if (!a || !*a)
        return 1;
    if (a[0] == '-')
        return 0;
    /* Leftover `sc create ... start= auto` tokens from launch arguments. */
    if (!_stricmp(a, "auto") || !_stricmp(a, "start") || !_strnicmp(a, "start=", 6))
        return 1;
    return 0;
}

static int append_quoted(char *out, size_t *used, size_t cap, const char *s)
{
    size_t i = *used;
    size_t need = 3;
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

static int append_raw(char *out, size_t *used, size_t cap, const char *s)
{
    size_t n = strlen(s);
    if (*used + 1 + n >= cap)
        return 0;
    if (*used)
        out[(*used)++] = ' ';
    memcpy(out + *used, s, n + 1);
    *used += n;
    return 1;
}

static char *read_file(const char *path)
{
    FILE *f;
    long sz;
    char *buf;
    f = fopen(path, "rb");
    if (!f)
        return NULL;
    if (fseek(f, 0, SEEK_END) != 0) {
        fclose(f);
        return NULL;
    }
    sz = ftell(f);
    if (sz < 0 || sz > 2 * 1024 * 1024) {
        fclose(f);
        return NULL;
    }
    rewind(f);
    buf = calloc((size_t)sz + 1, 1);
    if (!buf) {
        fclose(f);
        return NULL;
    }
    if (fread(buf, 1, (size_t)sz, f) != (size_t)sz) {
        free(buf);
        fclose(f);
        return NULL;
    }
    fclose(f);
    return buf;
}

static int json_string_after(const char *json, const char *anchor, const char *key, char *out, size_t cap)
{
    const char *from = json;
    const char *p;
    char pat[160];
    size_t n = 0;

    if (anchor && *anchor) {
        from = strstr(json, anchor);
        if (!from)
            return 0;
    }
    snprintf(pat, sizeof(pat), "\"%s\"", key);
    p = strstr(from, pat);
    if (!p)
        return 0;
    p = strchr(p + strlen(pat), ':');
    if (!p)
        return 0;
    p++;
    while (*p == ' ' || *p == '\t' || *p == '\n' || *p == '\r')
        p++;
    if (*p != '"')
        return 0;
    p++;
    while (*p && *p != '"' && n + 1 < cap) {
        if (*p == '\\' && p[1]) {
            p++;
            out[n++] = *p++;
            continue;
        }
        out[n++] = *p++;
    }
    out[n] = '\0';
    return 1;
}

static enum start_kind read_start_kind(void)
{
    char path[MAX_PATH];
    char value[64];
    char *json = NULL;
    const char *env = getenv("MUCK_SETTINGS_PATH");
    const char *appdata;

    memset(value, 0, sizeof(value));
    if (env && env[0])
        json = read_file(env);
    if (!json) {
        appdata = getenv("APPDATA");
        if (appdata) {
            snprintf(path, sizeof(path),
                     "%s\\MuckStore\\config\\com.tab2can.muckdpi.json", appdata);
            json = read_file(path);
        }
    }
    if (json) {
        json_string_after(json, NULL, "startType", value, sizeof(value));
        free(json);
    }
    if (!_stricmp(value, "automaticDelayed") || !_stricmp(value, "automatic-delayed"))
        return START_AUTOMATIC_DELAYED;
    if (!_stricmp(value, "manual"))
        return START_MANUAL;
    if (!_stricmp(value, "disabled"))
        return START_DISABLED;
    return START_AUTOMATIC;
}

static int store_launch_args(char *out, size_t cap)
{
    char path[MAX_PATH];
    char *json;
    const char *appdata = getenv("APPDATA");
    const char *id = getenv("MUCK_PROGRAM_ID");
    char anchor[128];

    if (!appdata)
        return 0;
    if (!id || !id[0])
        id = "com.tab2can.muckdpi";
    snprintf(path, sizeof(path), "%s\\MuckStore\\installed.json", appdata);
    json = read_file(path);
    if (!json)
        return 0;
    snprintf(anchor, sizeof(anchor), "\"%s\"", id);
    if (!json_string_after(json, anchor, "launchArgs", out, cap)) {
        free(json);
        return 0;
    }
    free(json);
    return out[0] != '\0';
}

static const char *start_kind_name(enum start_kind kind)
{
    switch (kind) {
        case START_AUTOMATIC_DELAYED: return "automatic (delayed)";
        case START_MANUAL: return "manual";
        case START_DISABLED: return "disabled";
        default: return "automatic";
    }
}

void service_install_autostart(int argc, char *argv[])
{
    char exe[MAX_PATH];
    char binpath[32768];
    char launch[8192];
    size_t used = 0;
    int i, have_cli = 0;
    enum start_kind kind;
    DWORD win_start;
    SC_HANDLE scm, svc;
    SERVICE_DESCRIPTIONA desc;
    SERVICE_DELAYED_AUTO_START_INFO delayed;
    DWORD err;
    BOOL delayed_flag;

    kind = read_start_kind();
    win_start = SERVICE_AUTO_START;
    if (kind == START_MANUAL)
        win_start = SERVICE_DEMAND_START;
    else if (kind == START_DISABLED)
        win_start = SERVICE_DISABLED;

    if (GetModuleFileNameA(NULL, exe, MAX_PATH) == 0)
        return;
    if (!append_quoted(binpath, &used, sizeof(binpath), exe))
        return;

    memset(launch, 0, sizeof(launch));
    if (store_launch_args(launch, sizeof(launch))) {
        if (!append_raw(binpath, &used, sizeof(binpath), launch))
            return;
    } else {
        for (i = 1; i < argc; i++) {
            if (is_sc_noise(argv[i]))
                continue;
            have_cli = 1;
            if (!append_quoted(binpath, &used, sizeof(binpath), argv[i]))
                return;
        }
        (void)have_cli;
    }

    scm = OpenSCManagerA(NULL, NULL, SC_MANAGER_CONNECT | SC_MANAGER_CREATE_SERVICE);
    if (!scm) {
        puts("Could not open the service manager (need administrator).");
        return;
    }

    svc = CreateServiceA(
        scm, SERVICE_NAME, SERVICE_NAME,
        SERVICE_ALL_ACCESS,
        SERVICE_WIN32_OWN_PROCESS,
        win_start,
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
                svc, SERVICE_WIN32_OWN_PROCESS, win_start,
                SERVICE_ERROR_NORMAL, binpath, NULL, NULL, NULL, NULL, NULL, NULL)) {
            printf("Could not update the MuckDPI service (error %lu).\n", GetLastError());
            CloseServiceHandle(svc);
            CloseServiceHandle(scm);
            return;
        }
    }

    delayed.fDelayedAutostart = (kind == START_AUTOMATIC_DELAYED);
    delayed_flag = ChangeServiceConfig2A(svc, SERVICE_CONFIG_DELAYED_AUTO_START_INFO, &delayed);
    if (!delayed_flag && kind == START_AUTOMATIC_DELAYED)
        printf("Could not set delayed auto-start (error %lu).\n", GetLastError());

    desc.lpDescription = "MuckDPI — DPI circumvention. Start type and arguments come from Muck Store.";
    ChangeServiceConfig2A(svc, SERVICE_CONFIG_DESCRIPTION, &desc);
    CloseServiceHandle(svc);
    CloseServiceHandle(scm);
    printf("Windows service MuckDPI: %s\nCommand: %s\n", start_kind_name(kind), binpath);
}

void service_stop_if_running(void)
{
    SC_HANDLE scm, svc;
    SERVICE_STATUS st;
    int i;

    scm = OpenSCManagerA(NULL, NULL, SC_MANAGER_CONNECT);
    if (!scm)
        return;
    svc = OpenServiceA(scm, SERVICE_NAME, SERVICE_STOP | SERVICE_QUERY_STATUS);
    if (!svc) {
        CloseServiceHandle(scm);
        return;
    }
    ControlService(svc, SERVICE_CONTROL_STOP, &st);
    for (i = 0; i < 50; i++) {
        if (!QueryServiceStatus(svc, &st))
            break;
        if (st.dwCurrentState == SERVICE_STOPPED)
            break;
        Sleep(100);
    }
    CloseServiceHandle(svc);
    CloseServiceHandle(scm);
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
            break;
        default:
            break;
    }
    // Report current status
    SetServiceStatus(hStatus, &ServiceStatus);
    return;
}
