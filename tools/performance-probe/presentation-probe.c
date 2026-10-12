/* QA only: compositor presentation feedback, with no extra swap or readback. */
#define _GNU_SOURCE
#include <dlfcn.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

struct interface;
struct message { const char *name, *signature; const struct interface **types; };
struct interface { const char *name; int version, method_count; const struct message *methods; int event_count; const struct message *events; };
static const struct interface feedback_interface;
static const struct interface *feedback_types[2];
static const struct message presentation_methods[] = {{"destroy", "", NULL}, {"feedback", "on", feedback_types}};
static const struct message presentation_events[] = {{"clock_id", "u", NULL}};
static const struct interface presentation_interface = {"wp_presentation", 1, 2, presentation_methods, 1, presentation_events};
static const struct interface *output_types[1];
static const struct message feedback_events[] = {{"sync_output", "o", output_types}, {"presented", "uuuuuuu", NULL}, {"discarded", "", NULL}};
static const struct interface feedback_interface = {"wp_presentation_feedback", 1, 0, NULL, 3, feedback_events};
static void *display, *surface, *queue, *presentation;
static void *(*construct)(void *, uint32_t, const struct interface *, ...);
static int (*listen)(void *, void (**)(void), void *);
static void (*set_queue)(void *, void *), (*destroy)(void *);
static int (*pending)(void *, void *), (*roundtrip)(void *, void *);
static uint32_t (*proxy_id)(void *);
static int initialized, enabled, generation, clock_id = -1;
static int buffer_attached;
static uint64_t commits, buffer_commits;
struct row { uint64_t submit, presented, sequence, commit; uint32_t refresh, flags, outputs, first_output; int discarded, gen; };
static struct row rows[20000];
static int count;
static uint64_t now_ns(void) { struct timespec t; clock_gettime(CLOCK_MONOTONIC, &t); return (uint64_t)t.tv_sec*1000000000+t.tv_nsec; }
static void clock_event(void *data, void *proxy, uint32_t id) { clock_id = id; }
static void (*clock_listener[])(void) = {(void (*)(void))clock_event};
static void global_event(void *data, void *registry, uint32_t name, const char *type, uint32_t version) {
    if (!strcmp(type, "wp_presentation")) {
        presentation = construct(registry, 0, &presentation_interface, name, "wp_presentation", 1, NULL);
        set_queue(presentation, queue); listen(presentation, clock_listener, NULL);
    }
}
static void removed_event(void *data, void *proxy, uint32_t name) {}
static void (*registry_listener[])(void) = {(void (*)(void))global_event, (void (*)(void))removed_event};
static void output_event(void *data, void *proxy, void *output) {
    struct row *r=data; r->outputs++; if (!r->first_output && output) r->first_output=proxy_id(output);
}
static void save_row(void *proxy, struct row *r) {
    if (enabled && r->gen==generation && count<20000) rows[count++]=*r;
    destroy(proxy); free(r);
}
static void presented_event(void *data, void *proxy, uint32_t hi, uint32_t lo, uint32_t ns, uint32_t refresh, uint32_t seqhi, uint32_t seqlo, uint32_t flags) {
    struct row *r=data; r->presented=(((uint64_t)hi<<32)|lo)*1000000000+ns;
    r->refresh=refresh; r->sequence=((uint64_t)seqhi<<32)|seqlo; r->flags=flags; save_row(proxy,r);
}
static void discarded_event(void *data, void *proxy) { struct row *r=data; r->discarded=1; save_row(proxy,r); }
static void (*feedback_listener[])(void)={(void (*)(void))output_event,(void (*)(void))presented_event,(void (*)(void))discarded_event};

int rgds_presentation_init(void) {
    if (initialized) return presentation ? 1 : 0;
    initialized=1;
    void *sdl=dlopen("libSDL2-2.0.so.0",RTLD_NOW), *wl=dlopen("libwayland-client.so.0",RTLD_NOW);
    if (!sdl || !wl) return 0;
    void *(*window)(void)=dlsym(sdl,"SDL_GL_GetCurrentWindow");
    int (*wm_info)(void *, void *)=dlsym(sdl,"SDL_GetWindowWMInfo");
    void (*version)(void *)=dlsym(sdl,"SDL_GetVersion");
    unsigned char info[256]={0}; version(info);
    if (!window || !window()) { fprintf(stderr,"presentation QA: no current SDL window\n"); return 0; }
    if (!wm_info(window(),info) || *(int *)(info+4)!=6) {
        fprintf(stderr,"presentation QA: WM info failed or subsystem=%d\n",*(int *)(info+4)); return 0;
    }
    memcpy(&display,info+8,sizeof(void *)); memcpy(&surface,info+16,sizeof(void *));
    void *(*create_queue)(void *)=dlsym(wl,"wl_display_create_queue");
    construct=dlsym(wl,"wl_proxy_marshal_constructor"); listen=dlsym(wl,"wl_proxy_add_listener");
    set_queue=dlsym(wl,"wl_proxy_set_queue"); destroy=dlsym(wl,"wl_proxy_destroy");
    pending=dlsym(wl,"wl_display_dispatch_queue_pending"); roundtrip=dlsym(wl,"wl_display_roundtrip_queue"); proxy_id=dlsym(wl,"wl_proxy_get_id");
    if (!display || !surface || !construct || !listen || !create_queue) return 0;
    feedback_types[0]=dlsym(wl,"wl_surface_interface"); feedback_types[1]=&feedback_interface;
    output_types[0]=dlsym(wl,"wl_output_interface");
    queue=create_queue(display);
    void *registry=construct(display,1,dlsym(wl,"wl_registry_interface"),NULL);
    set_queue(registry,queue); listen(registry,registry_listener,NULL); roundtrip(display,queue);
    if (presentation) roundtrip(display,queue);
    destroy(registry);
    fprintf(stderr,"presentation QA: protocol=%d clock=%d\n",presentation!=NULL,clock_id);
    return presentation ? 1 : 0;
}
void rgds_presentation_start(void) { count=0; commits=0; buffer_commits=0; generation++; enabled=1; }
static void request_feedback(void) {
    struct row *r=calloc(1,sizeof(*r));
    if (!r) return;
    r->submit=now_ns(); r->gen=generation; r->commit=buffer_commits;
    void *feedback=construct(presentation,1,&feedback_interface,surface,NULL);
    if (!feedback) { free(r); return; }
    set_queue(feedback,queue); listen(feedback,feedback_listener,r);
}
/* The device's legacy and variadic marshal entry points forward through this
 * public nonvariadic API. Observe only the SDL surface, immediately before the
 * commit carrying an attached EGL buffer. No swap or display roundtrip added. */
union argument { int32_t i; uint32_t u; int32_t f; const char *s; void *o; uint32_t n; void *a; int32_t h; };
void *wl_proxy_marshal_array_flags(void *proxy, uint32_t opcode,
    const struct interface *interface, uint32_t version, uint32_t flags, union argument *args) {
    static void *(*next)(void *,uint32_t,const struct interface *,uint32_t,uint32_t,union argument *);
    if (!next) next=dlsym(RTLD_NEXT,"wl_proxy_marshal_array_flags");
    if (!next) {
        /* SDL loads Wayland locally on this device, outside RTLD_NEXT scope. */
        void *wl=dlopen("libwayland-client.so.0",RTLD_LAZY);
        if (wl) next=dlsym(wl,"wl_proxy_marshal_array_flags");
    }
    if (!next) { fprintf(stderr,"presentation QA: marshal lookup failed\n"); abort(); }
    /* EGL may use a queue wrapper for the same protocol object. */
    if (surface && proxy_id && proxy_id(proxy)==proxy_id(surface)) {
        if (opcode==1) buffer_attached=args && args[0].o!=NULL;
        if (opcode==6) {
            if (enabled) {
                commits++;
                if (buffer_attached) { buffer_commits++; if (presentation) request_feedback(); }
            }
            buffer_attached=0;
        }
    }
    return next(proxy,opcode,interface,version,flags,args);
}
void rgds_presentation_before(void) { if (presentation) pending(display,queue); }
void rgds_presentation_after(void) { if (presentation) pending(display,queue); }
int rgds_presentation_stop(const char *path) {
    if (presentation) pending(display,queue);
    enabled=0;
    FILE *f=fopen(path,"w"); if (!f) return -1;
    fprintf(stderr,"presentation QA: commits=%lu buffers=%lu feedback=%d\n",commits,buffer_commits,count);
    fprintf(f,"submit_ns,presented_ns,refresh_ns,sequence,flags,outputs,first_output,discarded,clock_id,buffer_commit,total_commits,total_buffers\n");
    for(int i=0;i<count;i++) { struct row *r=&rows[i]; fprintf(f,"%lu,%lu,%u,%lu,%u,%u,%u,%d,%d,%lu,%lu,%lu\n",r->submit,r->presented,r->refresh,r->sequence,r->flags,r->outputs,r->first_output,r->discarded,clock_id,r->commit,commits,buffer_commits); }
    fclose(f); return count;
}
