// Deliberately non-HLSL header that should be treated as opaque by the preprocessor.
// The content contains tokens that are invalid HLSL so it must be stripped.
typedef struct _OpaqueThing {
    int value;
    int other;
} OpaqueThing;

int OpaqueFunction(void* data);
