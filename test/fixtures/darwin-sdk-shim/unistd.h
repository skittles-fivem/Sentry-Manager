/* The real unistd plus the macOS-only parts the helper uses, and pathconf,
   which MinGW's unistd.h on Windows does not declare. */
#include_next <unistd.h>
#ifndef SHIM_UNISTD_H
#define SHIM_UNISTD_H
#include <stddef.h>
#define _PC_CASE_SENSITIVE 11
int getattrlist(const char *, void *, void *, size_t, unsigned int);
long pathconf(const char *, int);
#endif
