int service_register(int argc, char *argv[]);
void service_install_autostart(int argc, char *argv[]);
void service_stop_if_running(void);
void service_main(int argc, char *argv[]);
void service_controlhandler(DWORD request);
