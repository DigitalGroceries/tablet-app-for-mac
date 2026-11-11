// CI check (#39): load the Affinity plugin the way a host does and call it, on a real Mac.
#include <dlfcn.h>
#include <stdio.h>
#include <stdlib.h>
#include <stdint.h>

int main(int argc, char **argv)
{
    if (!dlopen("/System/Library/Frameworks/AppKit.framework/AppKit", RTLD_NOW)) { printf("AppKit: %s\n", dlerror()); return 2; }
    void *h = dlopen(argv[1], RTLD_NOW | RTLD_LOCAL);
    if (!h) { printf("LOAD FAILED: %s\n", dlerror()); return 1; }
    printf("loaded OK\n");
    void (*pm)(short, void *, intptr_t *, short *) = dlsym(h, "PluginMain");
    if (!pm) { printf("NO PluginMain: %s\n", dlerror()); return 1; }
    short r = 99; intptr_t d = 0;
    pm(1, 0, &d, &r);
    printf("PluginMain(1) result %d (expect -128)\n", r);
    return r == -128 ? 0 : 1;
}
