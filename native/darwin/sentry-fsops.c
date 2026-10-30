/*
 * sentry-fsops: the macOS helper for Sentry Six Manager.
 *
 *   sentry-fsops rename-excl FROM TO
 *   sentry-fsops volinfo PATH
 *
 * rename-excl atomically renames FROM to TO only if TO does not exist,
 * using renameatx_np(RENAME_EXCL | RENAME_NOFOLLOW_ANY). Node has no binding
 * for it. A symlink anywhere in either path gives ELOOP, so nothing outside
 * the canonical folders is ever touched. A filesystem without exclusive
 * rename (smbfs, for one) gives ENOTSUP.
 *
 * volinfo prints one JSON line about the volume that holds PATH:
 *   {"fstype":"apfs","local":true,"caseSensitive":false,"renameExcl":true,
 *    "hardLinks":true,"clone":true,"persistentIds":true}
 * from statfs and getattrlist(ATTR_VOL_CAPABILITIES). A capability counts
 * only when the volume marks it both valid and set.
 *
 * Same contract as native/noreplace-rename.c: exit 0 on success. Otherwise
 * prints the errno name (EEXIST, ENOENT, ENOTSUP, ...) on stdout and exits 1,
 * or prints EINVAL and exits 2 for bad usage. Every path must be absolute.
 *
 * Apple Silicon, macOS 13.5 or later (Node 24's minimum).
 */
#include <errno.h>
#include <fcntl.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>
#include <sys/attr.h>
#include <sys/mount.h>
#include <unistd.h>

#if defined(__APPLE__) && !defined(__arm64__)
#error "sentry-fsops is built for Apple Silicon (arm64) only"
#endif

/* In <sys/stdio.h> since macOS 11; older SDKs lack the define. */
#ifndef RENAME_NOFOLLOW_ANY
#define RENAME_NOFOLLOW_ANY 0x00000010
#endif
#ifndef VOL_CAP_INT_RENAME_EXCL
#define VOL_CAP_INT_RENAME_EXCL 0x00080000
#endif
#ifndef VOL_CAP_INT_CLONE
#define VOL_CAP_INT_CLONE 0x00010000
#endif

/* ENOTSUP (45) and EOPNOTSUPP (102) are different errnos on macOS. */
static const char *errno_name(int e) {
  switch (e) {
    case EEXIST: return "EEXIST";
    case ENOENT: return "ENOENT";
    case EINVAL: return "EINVAL";
    case ENOSYS: return "ENOSYS";
    case EXDEV: return "EXDEV";
    case EACCES: return "EACCES";
    case EPERM: return "EPERM";
    case EBUSY: return "EBUSY";
    case EIO: return "EIO";
    case ENOTDIR: return "ENOTDIR";
    case EISDIR: return "EISDIR";
    case ENOTEMPTY: return "ENOTEMPTY";
    case EROFS: return "EROFS";
    case ENOSPC: return "ENOSPC";
    case ETIMEDOUT: return "ETIMEDOUT";
    case EHOSTDOWN: return "EHOSTDOWN";
    case ESTALE: return "ESTALE";
    case EAGAIN: return "EAGAIN";
    case ENOTCONN: return "ENOTCONN";
    case EOPNOTSUPP: return "EOPNOTSUPP";
    case ENOTSUP: return "ENOTSUP";
    case ELOOP: return "ELOOP";
    case ENAMETOOLONG: return "ENAMETOOLONG";
#ifdef ENOTCAPABLE
    case ENOTCAPABLE: return "ENOTCAPABLE";
#endif
    case EDEADLK: return "EDEADLK";
    case EAUTH: return "EAUTH";
    case ENEEDAUTH: return "ENEEDAUTH";
    default: return "EIO";
  }
}

static int fail(void) {
  puts(errno_name(errno));
  return 1;
}

static int usage(void) {
  fputs("usage: sentry-fsops rename-excl /abs/from /abs/to\n"
        "       sentry-fsops volinfo /abs/path\n", stderr);
  puts("EINVAL");
  return 2;
}

static int rename_excl(const char *from, const char *to) {
  if (renameatx_np(AT_FDCWD, from, AT_FDCWD, to, RENAME_EXCL | RENAME_NOFOLLOW_ANY) == 0) return 0;
  return fail();
}

/* The reply getattrlist fills in: its length, then the capabilities. */
struct vol_caps_reply {
  uint32_t length;
  vol_capabilities_attr_t caps;
};
_Static_assert(sizeof(struct vol_caps_reply) == sizeof(uint32_t) + sizeof(vol_capabilities_attr_t),
  "the reply has no padding");

static int has_cap(const vol_capabilities_attr_t *caps, int group, uint32_t bit) {
  return (caps->valid[group] & bit) && (caps->capabilities[group] & bit);
}

/* JSON string without the quotes; fstype names are short ASCII. */
static void print_json_text(const char *s) {
  for (; *s; s++) {
    unsigned char c = (unsigned char)*s;
    if (c == '"' || c == '\\') printf("\\%c", c);
    else if (c < 0x20) printf("\\u%04x", c);
    else putchar(c);
  }
}

static const char *json_bool(int v) {
  return v ? "true" : "false";
}

static int volinfo(const char *target) {
  struct statfs sfs;
  if (statfs(target, &sfs) != 0) return fail();

  /* Ask the volume's root: volume attributes are about the mount. */
  struct attrlist want;
  memset(&want, 0, sizeof want);
  want.bitmapcount = ATTR_BIT_MAP_COUNT;
  want.volattr = ATTR_VOL_INFO | ATTR_VOL_CAPABILITIES;
  struct vol_caps_reply reply;
  memset(&reply, 0, sizeof reply);
  if (getattrlist(sfs.f_mntonname, &want, &reply, sizeof reply, 0) != 0) return fail();
  const vol_capabilities_attr_t *caps = &reply.caps;

  int case_sensitive;
  if (caps->valid[VOL_CAPABILITIES_FORMAT] & VOL_CAP_FMT_CASE_SENSITIVE) {
    case_sensitive = has_cap(caps, VOL_CAPABILITIES_FORMAT, VOL_CAP_FMT_CASE_SENSITIVE);
  } else {
    /* Not reported: ask the path itself. Unknown counts as insensitive. */
    case_sensitive = pathconf(target, _PC_CASE_SENSITIVE) == 1;
  }

  char fstype[sizeof sfs.f_fstypename + 1];
  memcpy(fstype, sfs.f_fstypename, sizeof sfs.f_fstypename);
  fstype[sizeof sfs.f_fstypename] = '\0';

  fputs("{\"fstype\":\"", stdout);
  print_json_text(fstype);
  printf("\",\"local\":%s,\"caseSensitive\":%s,\"renameExcl\":%s,\"hardLinks\":%s,\"clone\":%s,\"persistentIds\":%s}\n",
    json_bool((sfs.f_flags & MNT_LOCAL) != 0),
    json_bool(case_sensitive),
    json_bool(has_cap(caps, VOL_CAPABILITIES_INTERFACES, VOL_CAP_INT_RENAME_EXCL)),
    json_bool(has_cap(caps, VOL_CAPABILITIES_FORMAT, VOL_CAP_FMT_HARDLINKS)),
    json_bool(has_cap(caps, VOL_CAPABILITIES_INTERFACES, VOL_CAP_INT_CLONE)),
    json_bool(has_cap(caps, VOL_CAPABILITIES_FORMAT, VOL_CAP_FMT_PERSISTENTOBJECTIDS)));
  /* A broken stdout cannot carry an errno name either. */
  return fflush(stdout) == 0 ? 0 : 1;
}

int main(int argc, char **argv) {
  if (argc == 4 && strcmp(argv[1], "rename-excl") == 0 && argv[2][0] == '/' && argv[3][0] == '/') {
    return rename_excl(argv[2], argv[3]);
  }
  if (argc == 3 && strcmp(argv[1], "volinfo") == 0 && argv[2][0] == '/') {
    return volinfo(argv[2]);
  }
  return usage();
}
