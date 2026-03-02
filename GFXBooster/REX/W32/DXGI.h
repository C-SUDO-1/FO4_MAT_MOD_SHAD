#pragma once

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <Windows.h>

#include <dxgi.h>
#include <d3d11.h>

namespace REX
{
    namespace W32
    {
        // HRESULT helper used by the plugin
        inline bool SUCCESS(HRESULT hr) { return SUCCEEDED(hr); }

        // Type aliases used throughout the plugin
        using ::IDXGISwapChain;

        using ::ID3D11Device;
        using ::ID3D11DeviceContext;

        using ::ID3D11Buffer;
        using ::ID3D11ShaderResourceView;

        using ::ID3D11VertexShader;
        using ::ID3D11PixelShader;

        using ::ID3D11ClassLinkage;
        using ::ID3D11ClassInstance;

        using ::D3D11_TEXTURE2D_DESC;
        using ::D3D11_BUFFER_DESC;
        using ::D3D11_SHADER_RESOURCE_VIEW_DESC;
        using ::D3D11_MAPPED_SUBRESOURCE;

        // Constant aliases used by the plugin
        static constexpr auto D3D11_USAGE_DYNAMIC = ::D3D11_USAGE_DYNAMIC;
        static constexpr auto D3D11_BIND_SHADER_RESOURCE = ::D3D11_BIND_SHADER_RESOURCE;
        static constexpr auto D3D11_CPU_ACCESS_WRITE = ::D3D11_CPU_ACCESS_WRITE;
        static constexpr auto D3D11_RESOURCE_MISC_BUFFER_STRUCTURED = ::D3D11_RESOURCE_MISC_BUFFER_STRUCTURED;
        static constexpr auto D3D11_SRV_DIMENSION_BUFFER = ::D3D11_SRV_DIMENSION_BUFFER;
        static constexpr auto DXGI_FORMAT_UNKNOWN = ::DXGI_FORMAT_UNKNOWN;
        static constexpr auto D3D11_MAP_WRITE_DISCARD = ::D3D11_MAP_WRITE_DISCARD;
    }
}
