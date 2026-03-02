#pragma once

// Compatibility logger shim for newer {fmt}/spdlog.
// - Avoids relying on C++20 std::source_location (many projects still build as C++17).
// - Avoids overload ambiguity between string literals and runtime format strings.
//
// Usage:
//   F4SE::log::info("Hello {}", name);          // compile-time checked (preferred)
//   F4SE::log::info(fmt::runtime(user_fmt), x); // runtime format string

#include <filesystem>
#include <optional>
#include <string_view>
#include <utility>

#include <spdlog/spdlog.h>
#include <spdlog/fmt/fmt.h>

// clang-format chokes hard on classes with attributes
#define F4SE_MAYBE_UNUSED [[maybe_unused]]

// IMPORTANT: Every line of this macro must end in a backslash.
// Missing a single '\\' will terminate the macro early and produce a cascade
// of confusing syntax errors.
#define F4SE_MAKE_SOURCE_LOGGER(a_func, a_level)                                                    \
	template <class... Args>                                                                        \
	struct F4SE_MAYBE_UNUSED a_func                                                                 \
	{                                                                                                \
		a_func() = delete;                                                                          \
		/* compile-time checked format strings (literals) */                                         \
		a_func(spdlog::format_string_t<Args...> a_fmt, Args&&... a_args)                             \
		{                                                                                            \
			spdlog::log(                                                                             \
				spdlog::source_loc{},                                                                \
				spdlog::level::a_level,                                                              \
				a_fmt,                                                                               \
				std::forward<Args>(a_args)...);                                                      \
		}                                                                                            \
		/* runtime format strings: must be wrapped with fmt::runtime(...) */                          \
		using _runtime_format_t = decltype(fmt::runtime(std::string_view{}));                        \
		a_func(_runtime_format_t a_fmt, Args&&... a_args)                                            \
		{                                                                                            \
			spdlog::log(                                                                             \
				spdlog::source_loc{},                                                                \
				spdlog::level::a_level,                                                              \
				a_fmt,                                                                               \
				std::forward<Args>(a_args)...);                                                      \
		}                                                                                            \
	};                                                                                               \
	template <class T, class... Args>                                                               \
	a_func(T&&, Args&&...) -> a_func<Args...>;

namespace F4SE
{
	namespace log
	{
		F4SE_MAKE_SOURCE_LOGGER(trace, trace);
		F4SE_MAKE_SOURCE_LOGGER(debug, debug);
		F4SE_MAKE_SOURCE_LOGGER(info, info);
		F4SE_MAKE_SOURCE_LOGGER(warn, warn);
		F4SE_MAKE_SOURCE_LOGGER(error, err);
		F4SE_MAKE_SOURCE_LOGGER(critical, critical);

		[[nodiscard]] std::optional<std::filesystem::path> log_directory();
	}
}

#undef F4SE_MAKE_SOURCE_LOGGER
#undef F4SE_MAYBE_UNUSED
