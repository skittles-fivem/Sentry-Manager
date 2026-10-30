/* macOS struct statfs (64-bit inode layout) from xnu bsd/sys/mount.h. */
#ifndef SHIM_SYS_MOUNT_H
#define SHIM_SYS_MOUNT_H
#include <stdint.h>
#define MFSTYPENAMELEN 16
#define MAXPATHLEN 1024
#define MNT_LOCAL 0x00001000
typedef struct { int32_t val[2]; } fsid_t;
struct statfs {
  uint32_t f_bsize;
  int32_t f_iosize;
  uint64_t f_blocks;
  uint64_t f_bfree;
  uint64_t f_bavail;
  uint64_t f_files;
  uint64_t f_ffree;
  fsid_t f_fsid;
  uint32_t f_owner;
  uint32_t f_type;
  uint32_t f_flags;
  uint32_t f_fssubtype;
  char f_fstypename[MFSTYPENAMELEN];
  char f_mntonname[MAXPATHLEN];
  char f_mntfromname[MAXPATHLEN];
  uint32_t f_flags_ext;
  uint32_t f_reserved[7];
};
int statfs(const char *, struct statfs *);
#endif
