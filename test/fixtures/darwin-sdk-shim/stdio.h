/* The real stdio plus what macOS adds from <sys/stdio.h>. */
#include_next <stdio.h>
#ifndef SHIM_STDIO_H
#define SHIM_STDIO_H
#define RENAME_SECLUDE 0x00000001
#define RENAME_SWAP 0x00000002
#define RENAME_EXCL 0x00000004
#define RENAME_NOFOLLOW_ANY 0x00000010
#define RENAME_RESOLVE_BENEATH 0x00000020
int renameatx_np(int, const char *, int, const char *, unsigned int);
#endif
