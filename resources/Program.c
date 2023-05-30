#ifdef _MSC_VER
#define inline __inline
#endif

typedef struct _Region {
	unsigned int depth;
} _Region;

extern _Region* _region_new();
extern void* _region_malloc(_Region* region, int size);
extern void _region_delete(_Region* region);
static inline _Region* _region_min(_Region* r1, _Region* r2) { return r1->depth < r2->depth ? r1 : r2;  }

typedef struct int_$Pointer int_$Pointer;
int_$Pointer test1(_Region* _return_region);

struct int_$Pointer {
    int* data;
    _Region* region;
};

int_$Pointer test1(_Region* _return_region) {
    /* Line 2: New variable declaration 'x' */
    int* x = _region_malloc(_return_region, sizeof(int));
    (*x) = 45U;

    return (int_$Pointer){ x, _return_region };
}

