#pragma once
// Dear ImGui: Platform Backend for Windows (Win32)
//
// This is a standalone header intended to work with either
// - the official Dear ImGui backend implementation, or
// - projects that vendor only the headers and compile the .cpp elsewhere.
//
// If you are using vcpkg imgui, you may already have these headers available.
// This file provides the standard declarations to satisfy includes.

#include "imgui.h"    // IMGUI_IMPL_API
#include <windows.h>

IMGUI_IMPL_API bool     ImGui_ImplWin32_Init(void* hwnd);
IMGUI_IMPL_API void     ImGui_ImplWin32_Shutdown();
IMGUI_IMPL_API void     ImGui_ImplWin32_NewFrame();

// Win32 message handler (to be called from your application's message handler).
// Return true if the message was handled by ImGui.
IMGUI_IMPL_API LRESULT  ImGui_ImplWin32_WndProcHandler(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam);

// Optional: set ImGui I/O flags used by some renderers/backends.
IMGUI_IMPL_API void     ImGui_ImplWin32_EnableDpiAwareness();
