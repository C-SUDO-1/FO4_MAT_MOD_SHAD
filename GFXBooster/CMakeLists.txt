cmake_minimum_required(VERSION 3.20)

# CMake 4.2+ removes FindBoost; CommonLibF4 still calls find_package(Boost ...)
# Tell CMake we acknowledge the new behavior to suppress dev warnings.
if(POLICY CMP0167)
  cmake_policy(SET CMP0167 NEW)
endif()

project(GFXBooster LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 23)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Ensure Windows API level is new enough for Boost.Atomic wait/wake primitives (WaitOnAddress/WakeByAddress*).
# These APIs exist on Windows 8+; we target Windows 10 explicitly.
add_compile_definitions(_WIN32_WINNT=0x0A00 WINVER=0x0A00 BOOST_USE_WINAPI_VERSION=0x0A00)

# Build as a CommonLibF4 (F4SE) plugin.
# Assumes CommonLibF4 exists at: ../CommonLibF4 (relative to this GFXBooster folder)
set(COMMONLIBF4_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/../CommonLibF4")

# CommonLibF4 typically lives in COMMONLIBF4_ROOT/CommonLibF4
add_subdirectory("${COMMONLIBF4_ROOT}/CommonLibF4" "${CMAKE_BINARY_DIR}/CommonLibF4")

# Ensure CommonLibF4 is compiled with C++23 (it uses C++20/23 features like 'requires')
if (TARGET CommonLibF4)
  target_compile_features(CommonLibF4 PUBLIC cxx_std_23)
  set_property(TARGET CommonLibF4 PROPERTY CXX_STANDARD 23)
  set_property(TARGET CommonLibF4 PROPERTY CXX_STANDARD_REQUIRED ON)

  # Propagate the same WinAPI level / Boost winapi version to CommonLibF4 too.
  target_compile_definitions(CommonLibF4 PUBLIC _WIN32_WINNT=0x0A00 WINVER=0x0A00 BOOST_USE_WINAPI_VERSION=0x0A00)
endif()

add_library(GFXBooster SHARED
    main.cpp
    Plugin.cpp
    Global.cpp
)

target_include_directories(GFXBooster PRIVATE "${CMAKE_CURRENT_SOURCE_DIR}")
target_precompile_headers(GFXBooster PRIVATE PCH.h)

# Most CommonLibF4 setups expose one of these targets.
target_link_libraries(GFXBooster PRIVATE CommonLibF4)

# Optional: nicer MSVC behavior
if (MSVC)
  target_compile_definitions(GFXBooster PRIVATE _CRT_SECURE_NO_WARNINGS)
endif()
