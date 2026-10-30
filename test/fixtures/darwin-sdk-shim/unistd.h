/* The real unistd plus the macOS-only parts the helper uses. */
#include_next <unistd.h>
#ifndef SHIM_UNISTD_H
#define SHIM_UNISTD_H
#include <stddef.h>
#define _PC_CASE_SENSITIVE 11
int getattrlist(const char *, void *, void *, size_t, unsigned int);
#endif
