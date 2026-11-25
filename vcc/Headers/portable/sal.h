/* Minimal stand-in for the MSVC CRT header of the same name, used when VCC
   preprocesses with a non-MSVC preprocessor (Linux, browser playground). */
#ifndef _VCC_PORTABLE_SAL_H
#define _VCC_PORTABLE_SAL_H
#define __in
#define __out
#define __inout
#define __in_opt
#define __out_opt
#define __inout_opt
#define __in_ecount(n)
#define __out_ecount(n)
#define __inout_ecount(n)
#define __in_bcount(n)
#define __out_bcount(n)
#define __inout_bcount(n)
#define __deref_out
#define __success(e)
#define _In_
#define _Out_
#define _Inout_
#define _In_opt_
#define _Out_opt_
#define _In_reads_(n)
#define _Out_writes_(n)
#endif
