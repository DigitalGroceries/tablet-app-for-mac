// Tablet App for Mac — "Smooth pen strokes" filter plugin for Affinity (#39, Zak, 11 Nov 2026).
//
// Why: macOS merges ("coalesces") mouse and pen movement points by default, so a busy drawing app only
// sees some of them and a fast curve becomes straight pieces. Our driver sends every pen point (about
// 200 a second on an Intuos4); Affinity doesn't switch the merging off while you draw. Blender fixed the
// same thing with [NSEvent setMouseCoalescingEnabled:NO] (2020). Affinity takes Photoshop-style filter
// plugins, which run inside Affinity, so this plugin makes that one call for Affinity.
//
// What it does, inside Affinity only:
//   - when Affinity loads it, and when you run Filters › Plugins › Tablet App for Mac › Smooth pen strokes:
//     switches the point merging off;
//   - keeps it off: on every pen/mouse press (and when the pen comes near) it switches it off again if
//     Affinity turned it back on;
//   - leaves the picture alone: the filter step itself is cancelled, so nothing is changed or added to
//     the history;
//   - writes one line per event to ~/Library/Logs/TabletAppForMac-Affinity.log, so a test can see what
//     happened.
//
// No headers on purpose: the few Objective-C runtime, libc and GCD functions are declared here, and the
// build links against small stubs of libSystem and libobjc (tools/affinity-plugin/*.tbd). That lets it build
// on any machine with clang and lld (scripts/build-affinity-plugin.sh).

#include <stdbool.h>
#include <stdint.h>

typedef void *id;
typedef void *SEL;
extern id objc_getClass(const char *name);
extern SEL sel_registerName(const char *name);
extern void objc_msgSend(void);

typedef struct FILE FILE;
extern FILE *fopen(const char *path, const char *mode);
extern int fprintf(FILE *f, const char *fmt, ...);
extern int fclose(FILE *f);
extern char *getenv(const char *name);
extern int snprintf(char *buf, unsigned long size, const char *fmt, ...);
extern long time(long *t);
extern int pthread_main_np(void);

struct dispatch_queue_s;
extern struct dispatch_queue_s _dispatch_main_q;
extern void dispatch_async_f(struct dispatch_queue_s *queue, void *context, void (*work)(void *));

static bool g_monitor;

static bool merging_on(void)
{
    id cls = objc_getClass("NSEvent");
    if (!cls) return false;
    return ((bool (*)(id, SEL))objc_msgSend)(cls, sel_registerName("isMouseCoalescingEnabled"));
}

static void merging_off(void)
{
    id cls = objc_getClass("NSEvent");
    if (!cls) return;
    ((void (*)(id, SEL, bool))objc_msgSend)(cls, sel_registerName("setMouseCoalescingEnabled:"), false);
}

static void log_line(const char *what, bool before, bool after)
{
    const char *home = getenv("HOME");
    if (!home) return;
    char path[1024];
    snprintf(path, sizeof path, "%s/Library/Logs/TabletAppForMac-Affinity.log", home);
    FILE *f = fopen(path, "a");
    if (!f) return;
    fprintf(f, "%ld %s: point merging was %s, now %s\n", time(0), what, before ? "on" : "off", after ? "on" : "off");
    fclose(f);
}

// Pen down, mouse down, pen comes near: switch merging off again if something switched it back on.
static id on_event(id event)
{
    if (merging_on())
    {
        merging_off();
        log_line("turned back off", true, merging_on());
    }
    return event;
}

static void apply(void *why)
{
    bool before = merging_on();
    merging_off();
    if (!g_monitor)
    {
        id cls = objc_getClass("NSEvent");
        if (cls)
        {
            // NSEventMaskLeftMouseDown | NSEventMaskRightMouseDown | NSEventMaskTabletProximity
            unsigned long long mask = (1ULL << 1) | (1ULL << 3) | (1ULL << 24);
            id (^handler)(id) = ^id(id event) { return on_event(event); };
            id monitor = ((id (*)(id, SEL, unsigned long long, id))objc_msgSend)(
                cls, sel_registerName("addLocalMonitorForEventsMatchingMask:handler:"), mask, (id)handler);
            if (monitor)
            {
                ((id (*)(id, SEL))objc_msgSend)(monitor, sel_registerName("retain"));
                g_monitor = true;
            }
        }
    }
    log_line((const char *)why, before, merging_on());
}

static void apply_on_main(const char *why)
{
    if (pthread_main_np()) apply((void *)why);
    else dispatch_async_f(&_dispatch_main_q, (void *)why, apply);
}

__attribute__((constructor)) static void on_load(void)
{
    apply_on_main("loaded");
}

// Photoshop filter entry point. Selector 0 is "About"; every other step is cancelled (-128,
// userCanceledErr) so the picture is never touched.
__attribute__((visibility("default"))) void PluginMain(short selector, void *filterRecord, intptr_t *data, short *result)
{
    (void)filterRecord;
    (void)data;
    apply_on_main(selector == 0 ? "about" : "run");
    if (result) *result = selector == 0 ? 0 : -128;
}
