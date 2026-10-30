/* macOS errno values (xnu bsd/sys/errno.h), not Linux's. */
#ifndef SHIM_ERRNO_H
#define SHIM_ERRNO_H
extern int *__error(void);
#define errno (*__error())
#define EPERM 1
#define ENOENT 2
#define EIO 5
#define EDEADLK 11
#define EACCES 13
#define EBUSY 16
#define EEXIST 17
#define EXDEV 18
#define ENOTDIR 20
#define EISDIR 21
#define EINVAL 22
#define ENOSPC 28
#define EROFS 30
#define EAGAIN 35
#define ENOTSUP 45
#define ENOTCONN 57
#define ETIMEDOUT 60
#define ELOOP 62
#define ENAMETOOLONG 63
#define EHOSTDOWN 64
#define ENOTEMPTY 66
#define ESTALE 70
#define ENOSYS 78
#define EAUTH 80
#define ENEEDAUTH 81
#define EOPNOTSUPP 102
#define ENOTCAPABLE 107
#endif
