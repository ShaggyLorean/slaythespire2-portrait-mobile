// MonoMod (bundled inside 0Harmony) P/Invokes glibc-specific names that
// bionic does not provide: __errno_location is the hard blocker, and the
// library names it asks for ("libc", "libdl.so.2") do not exist on Android.
// This shim exports exactly what MonoMod imports and forwards to bionic, so
// a DllImportResolver can point MonoMod here instead of at libc.
//
// Without it every Harmony patch fails on device with
//   TypeInitializationException -> EntryPointNotFoundException: __errno_location
// which is what kept the portrait patch layer from ever applying.

#include <dlfcn.h>
#include <fcntl.h>
#include <stdlib.h>
#include <stdio.h>
#include <errno.h>
#include <pthread.h>
#include <stddef.h>
#include <sys/mman.h>
#include <unistd.h>

#define SHIM __attribute__((visibility("default")))

SHIM int *__errno_location(void) { return __errno(); }

SHIM void *mm_mmap(void *addr, size_t len, int prot, int flags, int fd, off_t off) {
    return mmap(addr, len, prot, flags, fd, off);
}
SHIM int mm_mprotect(void *addr, size_t len, int prot) { return mprotect(addr, len, prot); }
SHIM int mm_munmap(void *addr, size_t len) { return munmap(addr, len); }
SHIM long mm_sysconf(int name) { return sysconf(name); }
SHIM int mm_getpagesize(void) { return getpagesize(); }

// Diagnostic used while bringing the detour engine up on Android: reports
// which memory operation the platform actually refuses, instead of leaving us
// with a bare errno surfaced as "Invalid argument".
SHIM int mm_probe(char *out, int len) {
    int n = 0;
    long page = sysconf(_SC_PAGESIZE);
    void *rw = mmap(NULL, (size_t)page, PROT_READ | PROT_WRITE,
                    MAP_PRIVATE | MAP_ANONYMOUS, -1, 0);
    int rw_errno = (rw == MAP_FAILED) ? errno : 0;

    int rwx = -1, rwx_errno = 0;
    if (rw != MAP_FAILED) {
        rwx = mprotect(rw, (size_t)page, PROT_READ | PROT_WRITE | PROT_EXEC);
        rwx_errno = rwx == 0 ? 0 : errno;
    }

    void *exec = mmap(NULL, (size_t)page, PROT_READ | PROT_WRITE | PROT_EXEC,
                      MAP_PRIVATE | MAP_ANONYMOUS, -1, 0);
    int exec_errno = (exec == MAP_FAILED) ? errno : 0;

    n = __builtin_snprintf(out, (size_t)len,
                           "page=%ld mmapRW=%d(%d) mprotectRWX=%d(%d) mmapRWX=%d(%d)",
                           page, rw != MAP_FAILED, rw_errno, rwx, rwx_errno,
                           exec != MAP_FAILED, exec_errno);
    if (rw != MAP_FAILED) munmap(rw, (size_t)page);
    if (exec != MAP_FAILED) munmap(exec, (size_t)page);
    return n;
}

// Second diagnostic: the syscalls MonoMod uses around the detour write.
// Android has no /tmp, so anything that assumes one fails here.
SHIM int mm_probe2(char *out, int len) {
    const char *tmpdir = getenv("TMPDIR");
    char tmpl[512];
    __builtin_snprintf(tmpl, sizeof(tmpl), "%s/monomodXXXXXX", tmpdir ? tmpdir : "/tmp");
    int fd = mkstemp(tmpl);
    int mkstemp_errno = fd < 0 ? errno : 0;
    if (fd >= 0) {
        close(fd);
        unlink(tmpl);
    }

    int fds[2];
    int pipe_rc = pipe(fds);
    int pipe_errno = pipe_rc != 0 ? errno : 0;
    if (pipe_rc == 0) {
        close(fds[0]);
        close(fds[1]);
    }

    long page = sysconf(_SC_PAGESIZE);
    void *probe = mmap(NULL, (size_t)page, PROT_READ | PROT_WRITE,
                       MAP_PRIVATE | MAP_ANONYMOUS, -1, 0);
    unsigned char vec = 0;
    int mincore_rc = probe == MAP_FAILED ? -1 : mincore(probe, (size_t)page, &vec);
    int mincore_errno = mincore_rc != 0 ? errno : 0;
    if (probe != MAP_FAILED) munmap(probe, (size_t)page);

    return __builtin_snprintf(out, (size_t)len,
                              "TMPDIR=%s mkstemp=%d(%d) pipe2=%d(%d) mincore=%d(%d)",
                              tmpdir ? tmpdir : "<unset>", fd >= 0, mkstemp_errno,
                              pipe_rc == 0, pipe_errno, mincore_rc == 0, mincore_errno);
}

// Lets the managed side point native code at a writable directory, since
// Android has no /tmp and MonoMod builds its temp paths from TMPDIR.
SHIM int mm_set_tmpdir(const char *path) { return setenv("TMPDIR", path, 1); }
