/* The getattrlist volume capability parts of xnu bsd/sys/attr.h. */
#ifndef SHIM_SYS_ATTR_H
#define SHIM_SYS_ATTR_H
#include <stdint.h>
typedef uint32_t attrgroup_t;
#define ATTR_BIT_MAP_COUNT 5
struct attrlist {
  unsigned short bitmapcount;
  uint16_t reserved;
  attrgroup_t commonattr;
  attrgroup_t volattr;
  attrgroup_t dirattr;
  attrgroup_t fileattr;
  attrgroup_t forkattr;
};
#define VOL_CAPABILITIES_FORMAT 0
#define VOL_CAPABILITIES_INTERFACES 1
#define VOL_CAPABILITIES_RESERVED1 2
#define VOL_CAPABILITIES_RESERVED2 3
typedef uint32_t vol_capabilities_set_t[4];
typedef struct vol_capabilities_attr {
  vol_capabilities_set_t capabilities;
  vol_capabilities_set_t valid;
} vol_capabilities_attr_t;
#define VOL_CAP_FMT_PERSISTENTOBJECTIDS 0x00000001
#define VOL_CAP_FMT_HARDLINKS 0x00000004
#define VOL_CAP_FMT_CASE_SENSITIVE 0x00000100
#define VOL_CAP_INT_CLONE 0x00010000
#define VOL_CAP_INT_RENAME_SWAP 0x00040000
#define VOL_CAP_INT_RENAME_EXCL 0x00080000
#define ATTR_VOL_CAPABILITIES 0x00020000
#define ATTR_VOL_INFO 0x80000000
#endif
