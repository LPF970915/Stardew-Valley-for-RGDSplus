#include <stddef.h>
#include "lz4.h"

#define EXPORT __attribute__((visibility("default")))

/* Match the three LWJGL entry points used by Stardew Valley's P/Invoke calls.
   The game passes null JNI arguments and direct byte-buffer pointers. */
EXPORT int Java_org_lwjgl_util_lz4_LZ4_LZ4_1compressBound(
    void *env, void *clazz, int input_size)
{
    (void)env;
    (void)clazz;
    return LZ4_compressBound(input_size);
}

EXPORT int Java_org_lwjgl_util_lz4_LZ4_nLZ4_1compress_1default(
    void *env, void *clazz, const char *src, char *dst,
    int src_size, int dst_capacity)
{
    (void)env;
    (void)clazz;
    if (src == NULL || dst == NULL || src_size < 0 || dst_capacity <= 0)
        return 0;
    return LZ4_compress_default(src, dst, src_size, dst_capacity);
}

EXPORT int Java_org_lwjgl_util_lz4_LZ4_nLZ4_1decompress_1safe(
    void *env, void *clazz, const char *src, char *dst,
    int compressed_size, int dst_capacity)
{
    (void)env;
    (void)clazz;
    if (src == NULL || dst == NULL || compressed_size < 0 || dst_capacity < 0)
        return -1;
    return LZ4_decompress_safe(src, dst, compressed_size, dst_capacity);
}
