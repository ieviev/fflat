// 15.6kb executable, this is as small as you can get with fflat
// compiled with: fflat zerolib_16kb.fsx --stdlib zero
// but this is almost completely useless, as you can't use Array, Console, etc.
#nowarn "9"
open System.Runtime.InteropServices
[<DllImport("libc")>]
extern void putchar(int c)
let w c = putchar (int c)
w 'h'
w 'e'
w 'l'
w 'l'
w 'o'
w ' '
w 'w'
w 'o'
w 'r'
w 'l'
w 'd'

