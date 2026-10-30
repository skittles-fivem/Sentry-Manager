/* The real fcntl plus AT_FDCWD as macOS has it (MinGW's fcntl.h on Windows
   has no *at() calls, so no AT_FDCWD either). */
#include_next <fcntl.h>
#ifndef SHIM_FCNTL_H
#define SHIM_FCNTL_H
#ifndef AT_FDCWD
#define AT_FDCWD -2
#endif
#endif
