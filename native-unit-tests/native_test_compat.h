#pragma once

// The standalone test executable is compiled by MSVC, while the bundled
// sparsehash headers are shared with IL2CPP's Clang-oriented build scripts.
#ifndef __attribute__
#define __attribute__(...)
#endif
