#pragma once
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <sddl.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mferror.h>
#include <atomic>
#include <cstdint>
#include <mutex>
#include <thread>
#include <vector>

// This harness exercises the production frame reader without camera registration,
// the global named pipe, a background reader, or Windows settings changes.
#define RETURN_HR_IF_NULL(hr, value) do { if ((value) == nullptr) return (hr); } while (false)
#define RETURN_HR_IF(hr, condition) do { if (condition) return (hr); } while (false)
#define RETURN_IF_FAILED(expression) do { const HRESULT result = (expression); if (FAILED(result)) return result; } while (false)
#define private public
#include "SimpleFrameGenerator.h"
#undef private
