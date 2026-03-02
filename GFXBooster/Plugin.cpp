#include <Global.h>
#include <PCH.h>

// Forward declare D3D11 interfaces for globals referenced across translation units
struct ID3D11Buffer;
struct ID3D11ShaderResourceView;

// Helper casts: CommonLibF4 uses REX::W32 D3D11 interface wrappers; the plugin also exposes some globals
// as the raw D3D11 COM interfaces. These are layout-compatible, so reinterpret_cast is fine.
inline REX::W32::ID3D11Buffer* AsRexD3D11Buffer(ID3D11Buffer* p) noexcept { return reinterpret_cast<REX::W32::ID3D11Buffer*>(p); }
inline REX::W32::ID3D11ShaderResourceView* AsRexD3D11SRV(ID3D11ShaderResourceView* p) noexcept { return reinterpret_cast<REX::W32::ID3D11ShaderResourceView*>(p); }
inline ID3D11Buffer* AsRawD3D11Buffer(REX::W32::ID3D11Buffer* p) noexcept { return reinterpret_cast<ID3D11Buffer*>(p); }
inline ID3D11ShaderResourceView* AsRawD3D11SRV(REX::W32::ID3D11ShaderResourceView* p) noexcept { return reinterpret_cast<ID3D11ShaderResourceView*>(p); }


// --- Variables ---

// Global Singletons
RE::BSGraphics::RendererData* g_rendererData = nullptr;
RE::PlayerCharacter* g_player = nullptr;

namespace detail
{
    template <class RD>
    constexpr bool has_member_depthStencilTargets = requires(RD* rd) { rd->depthStencilTargets; };

    template <class RD>
    constexpr bool has_member_depthStencils = requires(RD* rd) { rd->depthStencils; };

    template <class RD>
    constexpr bool has_member_renderWindows = requires(RD* rd) { rd->renderWindows; };

    template <class RD>
    constexpr bool has_member_device = requires(RD* rd) { rd->device; };

    template <class RD>
    constexpr bool has_member_context = requires(RD* rd) { rd->context; };

    inline auto* GetDXDevice() noexcept
    {
#if GFXBOOSTER_HAS_BSGRAPHICS_RENDERER
        if (auto* r = RE::BSGraphics::Renderer::GetSingleton(); r) {
            if constexpr (requires { r->GetDevice(); }) {
                return r->GetDevice();
            }
        }
#endif
        if (auto* rd = RE::GetRendererData(); rd) {
            if constexpr (has_member_device<decltype(*rd)>) {
                return rd->device;
            }
        }
        return static_cast<REX::W32::ID3D11Device*>(nullptr);
    }

    inline auto* GetDXContext() noexcept
    {
#if GFXBOOSTER_HAS_BSGRAPHICS_RENDERER
        if (auto* r = RE::BSGraphics::Renderer::GetSingleton(); r) {
            if constexpr (requires { r->GetContext(); }) {
                return r->GetContext();
            }
        }
#endif
        if (auto* rd = RE::GetRendererData(); rd) {
            if constexpr (has_member_context<decltype(*rd)>) {
                return rd->context;
            }
        }
        return static_cast<REX::W32::ID3D11DeviceContext*>(nullptr);
    }

    template <class RD>
    inline auto& GetDepthStencilArray(RD* rd)
    {
        if constexpr (has_member_depthStencilTargets<RD>) {
            return rd->depthStencilTargets;
        } else {
            return rd->depthStencils;
        }
    }

    template <class RD>
    inline auto& GetRenderWindowArray(RD* rd)
    {
        if constexpr (has_member_renderWindows<RD>) {
            return rd->renderWindows;
        } else {
            return rd->renderWindow;
        }
    }
}  // namespace detail
RE::ActorValue* g_actorValueInfo = nullptr;
RE::Sky* g_sky = nullptr;
// Global Shader DB
ShaderDB g_ShaderDB = {};
// Global trackers of current shader
int g_currentTextureDSIndices[128] = { -1 }; 
// Tell MyCreatePixelShader to skip analysing the shader when creating replacement shaders to avoid infinite recursion
bool g_isCreatingReplacementShader = false;
// Tell MyPSSetShaderResources to skip setting the SRV when creating replacement shaders to avoid infinite recursion
bool g_isSettingResourcesForReplacementShader = false;
// Global custom buffer data structure instance for updating CB13
GFXBoosterAccessData g_customBufferData = {};
// Global custom resource to pass data to shaders
ID3D11Buffer* g_customSRVBuffer = nullptr;
ID3D11ShaderResourceView* g_customSRV = nullptr;
// Global depth buffer SRV for shaders to read depth when DEPTHBUFFER_ON is enabled
REX::W32::ID3D11ShaderResourceView* g_depthSRV = nullptr;
// Global frame counter for slow updates
static uint32_t g_frameTick = 0;      // frame counter
// Global player values
static float    g_healthPerc = 1.0f; // value sampled 30 frames ago
static float    g_lastRad = 0.0f;   // value sampled 30 frames ago
static float    g_radDmg = 0.0f;    // calculated value send to the shader
// Global wind values
static float    g_windSpeed = 0.0f;     // value sampled 30 frames ago
static float    g_windAngle = 0.0f;     // value sampled 30 frames ago
static float    g_windTurbulence = 0.0f;// value sampled 30 frames ago
// UI: Compiler neon flash shader pointer
REX::W32::ID3D11PixelShader* g_flashPixelShader = nullptr;
// UI: Imgui WndProc hook variables
WNDPROC g_originalWndProc = nullptr;
extern LRESULT ImGui_ImplWin32_WndProcHandler(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam);
LRESULT CALLBACK ImGuiWndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam) {
    if (g_imguiInitialized && ImGui_ImplWin32_WndProcHandler(hwnd, msg, wParam, lParam))
        return true;
    return CallWindowProc(g_originalWndProc, hwnd, msg, wParam, lParam);
}
// UI: shader-list lock snapshot (when checked we show a frozen list)
static bool g_shaderListLocked = false;
static std::vector<void*> g_lockedShaderKeys; // snapshot of map keys (original shader pointer)
// UI: show/hide replaced shaders in the list
static bool g_showReplaced = true; // default enabled

// --- Hooks ---

// Hook IDXGISwapChain::Present (called once per frame to update the constant buffer and DEV GUI)
using Present_t = HRESULT(STDMETHODCALLTYPE*)(
    REX::W32::IDXGISwapChain* This,
    UINT SyncInterval,
    UINT Flags);
Present_t OriginalPresent = nullptr;
HRESULT STDMETHODCALLTYPE MyPresent(
    REX::W32::IDXGISwapChain* This,
    UINT SyncInterval,
    UINT Flags) {
    if (CUSTOMBUFFER_ON) {
        UpdateCustomBuffer_Internal();  // Update CB13
    }
    if (DEVGUI_ON && g_imguiInitialized) {
        ImGui_ImplDX11_NewFrame();
        ImGui_ImplWin32_NewFrame();
        ImGui::NewFrame();
        UIDrawShaderDebugOverlay();  // Just the UI drawing
        ImGui::Render();
        ImGui_ImplDX11_RenderDrawData(ImGui::GetDrawData());
    }
    return OriginalPresent(This, SyncInterval, Flags);
}

// Hook for ID3D11DeviceContext::PSSetShader to replace the Pixel shader
using PSSetShader_t = void(STDMETHODCALLTYPE*)(
    REX::W32::ID3D11DeviceContext* This,
    REX::W32::ID3D11PixelShader* pPixelShader,
    REX::W32::ID3D11ClassInstance* const* ppClassInstances,
    UINT NumClassInstances);
PSSetShader_t OriginalPSSetShader = nullptr;
void STDMETHODCALLTYPE MyPSSetShader(
    REX::W32::ID3D11DeviceContext* This,
    REX::W32::ID3D11PixelShader* pPixelShader,
    REX::W32::ID3D11ClassInstance* const* ppClassInstances,
    UINT NumClassInstances) {
    if (pPixelShader) {
        // Check if this shader is matched with a replacement shader in our DB
        if (g_ShaderDB.IsEntryMatched(pPixelShader)) {
            g_ShaderDB.SetEntryRecentlyUsed(pPixelShader, true); // Mark this shader as recently used for tracking
            auto* replacementPixelShader = g_ShaderDB.GetReplacementShader(pPixelShader);
            // Get the replacement shader for this original shader
            if (replacementPixelShader) {
                // Replace the shader with our replacement shader
                if (DEBUGGING) {
                    auto* matchedDefinition = g_ShaderDB.GetMatchedDefinition(pPixelShader);
                    GCollection::INFO("MyPSSetShader: Replacing pixel shader with matched replacement for definition '{}'", matchedDefinition ? matchedDefinition->id : "Unknown");
                }
                pPixelShader = replacementPixelShader;
                // Set our custom SRV for replacement shaders to use in their shader code
                This->PSSetShaderResources(CUSTOMBUFFER_SLOT, 1, &g_customSRV);
            } else {
                auto* matchedDefinition = g_ShaderDB.GetMatchedDefinition(pPixelShader);
                if (matchedDefinition && !matchedDefinition->buggy) {
                    if (DEBUGGING)
                        GCollection::INFO("MyPSSetShader: Shader is matched but no replacement shader found, trying to compile...");
                    if (CompileShader_Internal(matchedDefinition)) {
                        g_ShaderDB.SetReplacementShader(pPixelShader, matchedDefinition->loadedPixelShader);
                        if (DEBUGGING)
                            GCollection::INFO("MyPSSetShader: Compiled replacement shader for definition '{}'", matchedDefinition->id);
                        // Replace the shader with our custom one
                        if (DEBUGGING) {
                            GCollection::INFO("MyPSSetShader: Replacing pixel shader with newly compiled replacement for definition '{}'", matchedDefinition->id);
                        }
                        pPixelShader = g_ShaderDB.GetReplacementShader(pPixelShader);
                        // Set our custom SRV for replacement shaders to use in their shader code
                        This->PSSetShaderResources(CUSTOMBUFFER_SLOT, 1, &g_customSRV);
                    } else {
                        REX::WARN("MyPSSetShader: Failed to compile replacement shader for definition '{}'", matchedDefinition->id);
                        matchedDefinition->buggy = true; // Mark as failed to compile
                    }
                }
            }
        }
    }
    // Call original function with either the original or replacement shader
    OriginalPSSetShader(This, pPixelShader, ppClassInstances, NumClassInstances);
}

// Hook for ID3D11DeviceContext::VSSetShader to replace the Vertex shader
using VSSetShader_t = void(STDMETHODCALLTYPE*)(
    REX::W32::ID3D11DeviceContext* This,
    REX::W32::ID3D11VertexShader* pVertexShader,
    REX::W32::ID3D11ClassInstance* const* ppClassInstances,
    UINT NumClassInstances);
VSSetShader_t OriginalVSSetShader = nullptr;
void STDMETHODCALLTYPE MyVSSetShader(
    REX::W32::ID3D11DeviceContext* This,
    REX::W32::ID3D11VertexShader* pVertexShader,
    REX::W32::ID3D11ClassInstance* const* ppClassInstances,
    UINT NumClassInstances) {
        if (pVertexShader) {
        // Check if this shader is matched with a replacement shader in our DB
        if (g_ShaderDB.IsEntryMatched(pVertexShader)) {
            g_ShaderDB.SetEntryRecentlyUsed(pVertexShader, true); // Mark this shader as recently used for tracking
            auto* replacementVertexShader = g_ShaderDB.GetReplacementShader(pVertexShader);
            // Get the replacement shader for this original shader
            if (replacementVertexShader) {
                // Replace the shader with our replacement shader
                if (DEBUGGING) {
                    auto* matchedDefinition = g_ShaderDB.GetMatchedDefinition(pVertexShader);
                    GCollection::INFO("MyVSSetShader: Replacing vertex shader with matched replacement for definition '{}'", matchedDefinition ? matchedDefinition->id : "Unknown");
                }
                pVertexShader = replacementVertexShader;
                // Set our custom SRV for replacement shaders to use in their shader code
                This->VSSetShaderResources(CUSTOMBUFFER_SLOT, 1, &g_customSRV);
            } else {
                auto* matchedDefinition = g_ShaderDB.GetMatchedDefinition(pVertexShader);
                if (matchedDefinition && !matchedDefinition->buggy) {
                    if (DEBUGGING)
                        GCollection::INFO("MyVSSetShader: Shader is matched but no replacement shader found, trying to compile...");
                    if (CompileShader_Internal(matchedDefinition)) {
                        g_ShaderDB.SetReplacementShader(pVertexShader, matchedDefinition->loadedVertexShader);
                        if (DEBUGGING)
                            GCollection::INFO("MyVSSetShader: Compiled replacement shader for definition '{}'", matchedDefinition->id);
                        // Replace the shader with our custom one
                        if (DEBUGGING) {
                            GCollection::INFO("MyVSSetShader: Replacing vertex shader with newly compiled replacement for definition '{}'", matchedDefinition->id);
                        }
                        pVertexShader = g_ShaderDB.GetReplacementShader(pVertexShader);
                        // Set our custom SRV for replacement shaders to use in their shader code
                        This->VSSetShaderResources(CUSTOMBUFFER_SLOT, 1, &g_customSRV);
                    } else {
                        REX::WARN("MyVSSetShader: Failed to compile replacement shader for definition '{}'", matchedDefinition->id);
                        matchedDefinition->buggy = true; // Mark as failed to compile
                    }
                }
            }
        }
    }
    // Call original function with either the original or replacement shader
    OriginalVSSetShader(This, pVertexShader, ppClassInstances, NumClassInstances);
}

// --- SHADER CREATION ---
// Hook for ID3D11Device::CreatePixelShader to analyze and store the shader in the ShaderDB
using CreatePixelShader_t = HRESULT(STDMETHODCALLTYPE*)(
    REX::W32::ID3D11Device* This,
    const void* pShaderBytecode,
    SIZE_T BytecodeLength,
    REX::W32::ID3D11ClassLinkage* pClassLinkage,
    REX::W32::ID3D11PixelShader** ppPixelShader);
CreatePixelShader_t OriginalCreatePixelShader = nullptr;
HRESULT STDMETHODCALLTYPE MyCreatePixelShader(
    REX::W32::ID3D11Device* This,
    const void* pShaderBytecode,
    SIZE_T BytecodeLength,
    REX::W32::ID3D11ClassLinkage* pClassLinkage,
    REX::W32::ID3D11PixelShader** ppPixelShader) {
    if (g_isCreatingReplacementShader) {
        // If we're in the process of creating a replacement shader, skip all processing to avoid infinite recursion and just call the original function
        return OriginalCreatePixelShader(This, pShaderBytecode, BytecodeLength, pClassLinkage, ppPixelShader);
    }
    HRESULT hr = OriginalCreatePixelShader(This, pShaderBytecode, BytecodeLength, pClassLinkage, ppPixelShader);
    if (REX::W32::SUCCESS(hr) && ppPixelShader && *ppPixelShader) {
        // Store bytecode for later dumping
        std::vector<uint8_t> bytecode(BytecodeLength);
        memcpy(bytecode.data(), pShaderBytecode, BytecodeLength);
        auto hash = static_cast<std::uint32_t>(std::hash<std::string_view>{}(std::string_view((char*)bytecode.data(), bytecode.size())));
        // Check if we've already analyzed this shader
        if (g_ShaderDB.HasEntry(*ppPixelShader)) {
            // Already in database, skip re-analysis
            return hr;
        }
        // Get the ShaderDB entry for this shader, which will analyze the shader and find a matching definition if it exists
        ShaderDBEntry entry = AnalyzeShader_Internal(*ppPixelShader, nullptr, std::move(bytecode), BytecodeLength);
        // Create a shader DB entry for this shader
        g_ShaderDB.AddShaderEntry(std::move(entry));
    }
    return hr;
}

// Hook for ID3D11Device::CreateVertexShader to analyze and store the shader in the ShaderDB
using CreateVertexShader_t = HRESULT(STDMETHODCALLTYPE*)(
    REX::W32::ID3D11Device* This,
    const void* pShaderBytecode,
    SIZE_T BytecodeLength,
    REX::W32::ID3D11ClassLinkage* pClassLinkage,
    REX::W32::ID3D11VertexShader** ppVertexShader);
CreateVertexShader_t OriginalCreateVertexShader = nullptr;
HRESULT STDMETHODCALLTYPE MyCreateVertexShader(
    REX::W32::ID3D11Device* This,
    const void* pShaderBytecode,
    SIZE_T BytecodeLength,
    REX::W32::ID3D11ClassLinkage* pClassLinkage,
    REX::W32::ID3D11VertexShader** ppVertexShader) {
    // For simplicity, we won't analyze vertex shaders for matching and replacement in this example, but we will track them for dumping if needed
    HRESULT hr = OriginalCreateVertexShader(This, pShaderBytecode, BytecodeLength, pClassLinkage, ppVertexShader);
    if (REX::W32::SUCCESS(hr) && ppVertexShader && *ppVertexShader) {
        // Store bytecode for later dumping
        std::vector<uint8_t> bytecode(BytecodeLength);
        memcpy(bytecode.data(), pShaderBytecode, BytecodeLength);
        auto hash = static_cast<std::uint32_t>(std::hash<std::string_view>{}(std::string_view((char*)bytecode.data(), bytecode.size())));
        // Check if we've already analyzed this shader
        if (g_ShaderDB.HasEntry(*ppVertexShader)) {
            // Already in database, skip re-analysis
            return hr;
        }
        // Get the ShaderDB entry for this shader, which will analyze the shader and find a matching definition if it exists
        ShaderDBEntry entry = AnalyzeShader_Internal(nullptr, *ppVertexShader, std::move(bytecode), BytecodeLength);
        // Create a shader DB entry for this shader
        g_ShaderDB.AddShaderEntry(std::move(entry));
    }
    return hr;
}

// -- Structs ---

// --- Functions ---

// Analyze the shader bytecode to extract info for matching and potential replacement.
ShaderDBEntry AnalyzeShader_Internal(REX::W32::ID3D11PixelShader* pixelShader, REX::W32::ID3D11VertexShader* vertexShader, std::vector<uint8_t> bytecode, SIZE_T BytecodeLength) {
    ShaderDBEntry entry{};
    if (!pixelShader && !vertexShader || bytecode.empty()) return entry;
    void* shader = nullptr;
    if (pixelShader) {
        shader = static_cast<void*>(pixelShader);
    } else if (vertexShader) {
        shader = static_cast<void*>(vertexShader);
    }
    entry.originalShader = shader;
    if (pixelShader) {
        entry.type = ShaderType::Pixel;
    } else if (vertexShader) {
        entry.type = ShaderType::Vertex;
    }
    entry.bytecode = std::move(bytecode);
    auto hash = static_cast<std::uint32_t>(std::hash<std::string_view>{}(std::string_view((char*)entry.bytecode.data(), entry.bytecode.size())));
    entry.hash = hash;
    entry.size = BytecodeLength;
    // Analyze the shader entry
    if (ReflectShader_Internal(entry)) {
        if (DEBUGGING) {
            GCollection::INFO("AnalyzeShader_Internal: Shader reflection of {} Shader successful. Hash={:08X}, Size={}", entry.type == ShaderType::Vertex ? "Vertex" : "Pixel", hash, entry.size);
        }
    } else {
        REX::WARN("AnalyzeShader_Internal: Shader reflection failed for {} Shader with hash {:08X} and size {} bytes.", entry.type == ShaderType::Vertex ? "Vertex" : "Pixel", hash, entry.size);
    }
    // Validate entry before returning
    if (!entry.hash || !entry.size) {
        entry.SetValid(false);
        return entry; // Invalid entry, do not add to ShaderDB
    } else {
        entry.SetValid(true);
    }
    // Compare to shader definitions in our INI and find a match based on filters
    // If we find a match, we can store the compiled replacement shader in the entry for quick access during rendering.
    std::shared_lock lock(g_shaderDefinitions.mutex);
    for (ShaderDefinition* def : g_shaderDefinitions.definitions) {
        if (def->active && DoesEntryMatchDefinition_Internal(entry, def)) {
            entry.SetMatched(true);
            entry.matchedDefinition = def; // Store the matched definition for later use during shader compilation
            if (DEVELOPMENT && def->log) {
                GCollection::INFO("AnalyzeShader_Internal: ------------------------------------------------");
                GCollection::INFO("AnalyzeShader_Internal: Found matching shader definition '{}' for {} shader with hash {:08X} and size {} bytes.", def->id, entry.type == ShaderType::Vertex ? "Vertex" : "Pixel", hash, entry.size);
                GCollection::INFO(" - Shader CB Sizes: {},{},{},{},{},{},{},{},{},{},{},{},{},{}", 
                    entry.expectedCBSizes[0], entry.expectedCBSizes[1], entry.expectedCBSizes[2], entry.expectedCBSizes[3],
                    entry.expectedCBSizes[4], entry.expectedCBSizes[5], entry.expectedCBSizes[6], entry.expectedCBSizes[7],
                    entry.expectedCBSizes[8], entry.expectedCBSizes[9], entry.expectedCBSizes[10], entry.expectedCBSizes[11],
                    entry.expectedCBSizes[12], entry.expectedCBSizes[13]);
                GCollection::INFO(" - Shader Texture Register Slots: {}", entry.textureSlots.empty() ? "None" : "");
                for (const auto& slot : entry.textureSlots) {
                    GCollection::INFO("   - Slot: t{}", slot);
                }
                GCollection::INFO(" - Shader Texture Dimensions: {}", entry.textureDimensions.empty() ? "None" : "");
                for (const auto& [dimension, slot] : entry.textureDimensions) {
                    GCollection::INFO("   - Dimension: {}, Slot: t{}", dimension, slot);
                }
                GCollection::INFO(" - Shader Texture Usage Bitmask: 0x{:08X}", entry.textureSlotMask);
                GCollection::INFO(" - Shader Texture Dimension Bitmask: 0x{:08X}", entry.textureDimensionMask);
                GCollection::INFO(" - Shader Input Texture Count: {}", entry.inputTextureCount != -1 ? std::to_string(entry.inputTextureCount) : "X");
                GCollection::INFO(" - Shader Input Count: {}", entry.inputCount != -1 ? std::to_string(entry.inputCount) : "X");
                GCollection::INFO(" - Shader Input Mask: 0x{:08X}", entry.inputMask);
                GCollection::INFO(" - Shader Output Count: {}", entry.outputCount != -1 ? std::to_string(entry.outputCount) : "X");
                GCollection::INFO(" - Shader Output Mask: 0x{:08X}", entry.outputMask);
                GCollection::INFO("AnalyzeShader_Internal: ------------------------------------------------");
            }
            if (DEVELOPMENT && def->dump && !entry.IsDumped()) {
                DumpOriginalShader_Internal(entry, def);
                entry.SetDumped(true);
            }
            break; // Stop checking after the first match to keep priorities based on order in INI
        }
    }
    return entry;
}

// Compile the HLSL shaders that were defined in the INI for each shader
bool CompileShader_Internal(ShaderDefinition* def) {
    if (!def) return false;
    // Check if already compiled
    if (def->loadedPixelShader || def->loadedVertexShader) {
        REX::WARN("CompileShader_Internal: Shader '{}' is already compiled. Skipping compilation.", def->id);
        return true;
    }
    // Check the file exists
    std::ifstream shaderFile(def->shaderFile, std::ios::binary);
    if (!shaderFile.good()) {
        REX::WARN("CompileShader_Internal: Shader file not found: {}", def->shaderFile.string());
        return false;
    }
    // Compile the shader source code
    std::string shaderSource((std::istreambuf_iterator<char>(shaderFile)), std::istreambuf_iterator<char>());
    shaderFile.close();
    std::string targetProfile = "ps_5_0"; // Default to pixel shader model 5.0
    if (def->type == ShaderType::Vertex) {
        targetProfile = "vs_5_0";
    } else if (def->type == ShaderType::Pixel) {
        targetProfile = "ps_5_0";
    } else {
        REX::WARN("CompileShader_Internal: Invalid shader type for shader '{}'. Defaulting to pixel shader.", def->id);
    }
    if (!g_rendererData || !detail::GetDXDevice()) {
        REX::WARN("CompileShader_Internal: Renderer device not available. Cannot compile shader '{}'", def->id);
        return false;
    }
    REX::W32::ID3D11Device* device = detail::GetDXDevice();
    ID3DBlob* errorBlob = nullptr;
    ID3DInclude* includeHandler = new ShaderIncludeHandler(); // Custom include handler to resolve #include directives relative to the plugin directory
    HRESULT hr = D3DCompile(
        shaderSource.c_str(),
        shaderSource.size(),
        def->id.c_str(),
        nullptr,
        includeHandler,
        "main",
        targetProfile.c_str(),
        D3DCOMPILE_OPTIMIZATION_LEVEL3,
        0,
        &def->compiledShader,
        &errorBlob
    );
    delete includeHandler;
    if (!REX::W32::SUCCESS(hr)) {
        if (errorBlob) {
            REX::WARN("CompileShader_Internal: Shader compilation failed: {}", static_cast<const char*>(errorBlob->GetBufferPointer()));
            errorBlob->Release();
        }
        return false;
    }
    if (errorBlob) errorBlob->Release();
    // Set flag to prevent hook from analyzing the shader
    g_isCreatingReplacementShader = true;
    // Create the actual shader object from the compiled bytecode
    if (def->type == ShaderType::Vertex) {
        hr = device->CreateVertexShader(
            def->compiledShader->GetBufferPointer(),
            def->compiledShader->GetBufferSize(),
            nullptr,
            &def->loadedVertexShader
        );
    } else {
        hr = device->CreatePixelShader(
            def->compiledShader->GetBufferPointer(),
            def->compiledShader->GetBufferSize(),
            nullptr,
            &def->loadedPixelShader
        );
    }
    // Reset flag after creation
    g_isCreatingReplacementShader = false;
    if (!REX::W32::SUCCESS(hr)) {
        if (def->type == ShaderType::Vertex) {
            REX::WARN("CompileShader_Internal: Failed to create vertex shader for '{}'", def->id);
        } else {
            REX::WARN("CompileShader_Internal: Failed to create pixel shader for '{}'", def->id);
        }
        return false;
    }
    if (DEBUGGING) {
        if (def->type == ShaderType::Vertex) {
            GCollection::INFO("CompileShader_Internal: {} compiled successfully! Bytecode size: {} bytes", def->shaderFile.string(), def->compiledShader->GetBufferSize());
        } else {
            GCollection::INFO("CompileShader_Internal: {} compiled successfully! Bytecode size: {} bytes", def->shaderFile.string(), def->compiledShader->GetBufferSize());
        }
    }
    return true;
}

// All provided requirements must match for the function to return true.
bool DoesEntryMatchDefinition_Internal(ShaderDBEntry const& entry, ShaderDefinition* def) {
    // Basic checks
    if (!entry.valid) return false;
    if (!def) return false;
    if (!def->active) return false;
    // Check shader type
    if (def->type == ShaderType::Pixel && entry.type != ShaderType::Pixel) return false;
    if (def->type == ShaderType::Vertex && entry.type != ShaderType::Vertex) return false;
    // Check hash[es] if specified
    if (def->hash.size() != 0) {
        bool hashMatch = false;
        for (const auto& hash : def->hash) {
            if (entry.hash == hash) {
                hashMatch = true;
                break;
            }
        }
        if (!hashMatch) {
            return false;
        }
    }
    // Check size requirement if specified
    if (!def->sizeRequirements.empty()) {
        for (const auto& req : def->sizeRequirements) {
            if (req.op == SizeOp::Equal && entry.size != req.value) return false;
            if (req.op == SizeOp::Greater && entry.size <= req.value) return false;
            if (req.op == SizeOp::Less && entry.size >= req.value) return false;
        }
    }
    // Check constant buffer sizes
    for (const auto& [size, slot] : def->bufferSizes) {
        // Handle size@ without slot (any slot)
        if (slot < 0) {
            bool anySlotMatches = false;
            for (int i = 0; i < 14; i++) {
                if (entry.expectedCBSizes[i] == size) {
                    anySlotMatches = true;
                    break;
                }
            }
            if (!anySlotMatches) return false;
        // Handle size@slot (specific slot)
        } else if (slot >= 0 && slot < 14) {
        if (entry.expectedCBSizes[slot] != size)
            return false;
        }
    }
    // Check texture slots
    if (def->textureSlotMask != 0 && ((entry.textureSlotMask & def->textureSlotMask) != def->textureSlotMask))
        return false;
    // Check texture dimensions
    if (def->textureDimensionMask != 0 && ((entry.textureDimensionMask & def->textureDimensionMask) != def->textureDimensionMask))
        return false;
    // Check input texture count if specified
    if (!def->inputTextureCountRequirements.empty()) {
        for (const auto& req : def->inputTextureCountRequirements) {
            if (req.op == SizeOp::Equal && entry.inputTextureCount != req.value) return false;
            if (req.op == SizeOp::Greater && entry.inputTextureCount <= req.value) return false;
            if (req.op == SizeOp::Less && entry.inputTextureCount >= req.value) return false;
        }
    }
    // Check input count if specified
    if (!def->inputCountRequirements.empty()) {
        for (const auto& req : def->inputCountRequirements) {
            if (req.op == SizeOp::Equal && entry.inputCount != req.value) return false;
            if (req.op == SizeOp::Greater && entry.inputCount <= req.value) return false;
            if (req.op == SizeOp::Less && entry.inputCount >= req.value) return false;
        }
    }
    // Check input mask if specified
    if (def->inputMask != 0 && (entry.inputMask & def->inputMask) != def->inputMask)
        return false;
    // Check output count if specified
    if (!def->outputCountRequirements.empty()) {
        for (const auto& req : def->outputCountRequirements) {
            if (req.op == SizeOp::Equal && entry.outputCount != req.value) return false;
            if (req.op == SizeOp::Greater && entry.outputCount <= req.value) return false;
            if (req.op == SizeOp::Less && entry.outputCount >= req.value) return false;
        }
    }
    // Check output mask if specified
    if (def->outputMask != 0 && (entry.outputMask & def->outputMask) != def->outputMask)
        return false;
    // It matches all provided requirements
    return true;
}

// Dump the original shader bytecode to a file for analysis
void DumpOriginalShader_Internal(ShaderDBEntry const& entry, ShaderDefinition* def) {
    if (!def->dump || !entry.IsValid() || entry.IsDumped()) return;
    // Schedule the dump on the game's task queue
    if (g_taskInterface) {
        // Capture entry and def by value to ensure they remain valid in the task
        g_taskInterface->AddTask([type=entry.type,
                                  hash=entry.hash,
                                  size=entry.size,
                                  bytecode=entry.bytecode,
                                  expectedCBSizes=[&entry]() { 
                                        std::array<std::uint32_t, 14> arr; 
                                        std::memcpy(arr.data(), entry.expectedCBSizes, sizeof(entry.expectedCBSizes)); 
                                        return arr; 
                                    }(),
                                  textureSlots=entry.textureSlots,
                                  textureDimensions=entry.textureDimensions,
                                  textureSlotMask=entry.textureSlotMask,
                                  textureDimensionMask=entry.textureDimensionMask,
                                  inputTextureCount=entry.inputTextureCount,
                                  inputCount=entry.inputCount,
                                  inputMask=entry.inputMask,
                                  outputCount=entry.outputCount,
                                  outputMask=entry.outputMask,
                                  def](){
            std::filesystem::path dumpPath = g_pluginPath / "GFXBoosterDumps" / def->id;
            std::filesystem::create_directories(dumpPath);
            if (def->dump && !bytecode.empty()) {
                std::string binFilename = std::format("{:08X}_original_{}_shader_{}_.bin", hash, (type == ShaderType::Pixel ? "ps" : "vs"), def->id);
                std::string asmFilename = std::format("{:08X}_original_{}_shader_{}_.asm", hash, (type == ShaderType::Pixel ? "ps" : "vs"), def->id);
                std::string logFilename = std::format("{:08X}_original_{}_shader_{}_.txt", hash, (type == ShaderType::Pixel ? "ps" : "vs"), def->id);
                std::filesystem::path binPath = dumpPath / binFilename;
                std::filesystem::path asmPath = dumpPath / asmFilename;
                std::filesystem::path logPath = dumpPath / logFilename;
                // Check if files already exist
                if (DEBUGGING && std::filesystem::exists(binPath)) {
                    REX::WARN("DumpOriginalShader_Internal: Binary file already exists, skipping: {}", binPath.string());
                    return;
                }
                // Dump bytecode to binary file
                std::ofstream binFile(binPath, std::ios::binary);
                binFile.write(reinterpret_cast<const char*>(bytecode.data()), bytecode.size());
                binFile.close();
                // Also disassemble to text
                ID3DBlob* disassembly = nullptr;
                HRESULT hr = D3DDisassemble(bytecode.data(), bytecode.size(), 0, nullptr, &disassembly);
                if (REX::W32::SUCCESS(hr) && disassembly) {
                    std::ofstream asmFile(asmPath);
                    asmFile.write(static_cast<const char*>(disassembly->GetBufferPointer()), disassembly->GetBufferSize());
                    asmFile.close();
                    disassembly->Release();
                }
                // Write a log file in the format of the Shader.ini
                std::ofstream logFile(logPath);
                // Write INI section header
                logFile << "[" << def->id << "]" << std::endl;
                logFile << "active=true" << std::endl;
                logFile << "priority=0" << std::endl;
                logFile << "type=" << (type == ShaderType::Pixel ? "ps" : "vs") << std::endl;
                logFile << "hash=0x" << std::hex << std::uppercase << hash << std::dec << std::endl;
                // Size as exact match in parentheses
                logFile << "size=(" << size << ")" << std::endl;
                // Buffer sizes in format: size@slot,size@slot
                logFile << "buffersize=";
                bool firstBuffer = true;
                for (int i = 0; i < 14; ++i) {
                    if (expectedCBSizes[i] > 0) {
                        if (!firstBuffer) logFile << ",";
                        logFile << expectedCBSizes[i] << "@" << i;
                        firstBuffer = false;
                    }
                }
                logFile << std::endl;
                // Textures in format: 0,1,2,...
                logFile << "textures=";
                if (!textureSlots.empty()) {
                    bool firstSlot = true;
                    for (const auto& slot : textureSlots) {
                        if (!firstSlot) logFile << ",";
                        logFile << slot;
                        firstSlot = false;
                    }
                }
                logFile << std::endl;
                // Texture dimensions in format: dimension@slot
                logFile << "textureDimensions=";
                if (!textureDimensions.empty()) {
                    bool firstDim = true;
                    for (const auto& [dimension, slot] : textureDimensions) {
                        if (!firstDim) logFile << ",";
                        logFile << dimension << "@" << slot;
                        firstDim = false;
                    }
                }
                logFile << std::endl;
                // Bitmasks
                logFile << "textureSlotMask=0x" << std::hex << std::uppercase << textureSlotMask << std::dec << std::endl;
                logFile << "textureDimensionMask=0x" << std::hex << std::uppercase << textureDimensionMask << std::dec << std::endl;
                // Counts in parentheses
                logFile << "inputTextureCount=(" << inputTextureCount << ")" << std::endl;
                logFile << "inputcount=(" << inputCount << ")" << std::endl;
                logFile << "inputMask=0x" << std::hex << std::uppercase << inputMask << std::dec << std::endl;
                logFile << "outputcount=(" << outputCount << ")" << std::endl;
                logFile << "outputMask=0x" << std::hex << std::uppercase << outputMask << std::dec << std::endl;
                // Shader file (empty, user needs to add)
                logFile << "shader=;" << hash << "_replacement.hlsl" << std::endl;
                logFile << "log=true" << std::endl;
                logFile << "dump=true" << std::endl;
                // Close section
                logFile << "[/" << def->id << "]" << std::endl;
                logFile.close();
                if (DEBUGGING)
                    GCollection::INFO("DumpOriginalShader_Internal: Dumped original shader for ID {} to disk for analysis. Binary: {}, Disassembly: {}, Log: {}", def->id, binFilename, asmFilename, logFilename);
            } else {
                if (DEBUGGING)
                    REX::WARN("DumpOriginalShader_Internal: Failed to dump shader for ID {} - either dumping is disabled or bytecode is not available.", def->id);
            }
        });
    } else {
        REX::WARN("DumpOriginalShader_Internal: Failed to dump shader for ID {} - task interface not available.", def->id);
    }
}

REX::W32::ID3D11ShaderResourceView* GetDepthBufferSRV_Internal() {
    if (!g_rendererData) {
        REX::WARN("GetDepthBufferSRV_Internal: RendererData not available.");
        return nullptr;
    }
    REX::W32::ID3D11ShaderResourceView* depthSRV = nullptr;
    for (int i = 0; i < 13; ++i) {
        auto possible = detail::GetDepthStencilArray(g_rendererData)[i].srViewDepth;
        if (possible) { depthSRV = possible; break; }
    }
    return depthSRV;
}

// Disassemble the shader bytecode and parse it to find out details about the shader
// Normal reflection API does not provide all the info we need and it unreliable
bool ReflectShader_Internal(ShaderDBEntry& entry) {
    if (entry.bytecode.empty()) return false;
    // We disassemble the shader and parse it manually to fill in our entry data.
    ID3DBlob* disassembly = nullptr;
    HRESULT hr = D3DDisassemble(entry.bytecode.data(), entry.bytecode.size(), 0, nullptr, &disassembly);
    if (!REX::W32::SUCCESS(hr) || !disassembly) {
        REX::WARN("ReflectShader_Internal: Failed to disassemble shader bytecode for reflection.");
        return false;
    }
    std::string disasmStr(static_cast<const char*>(disassembly->GetBufferPointer()), disassembly->GetBufferSize());
    // Define regexes as static once
    static const auto regexFlags = std::regex_constants::optimize | std::regex_constants::icase;
    // Catch t# registers
    static std::regex texRegex(R"(dcl_resource_(\w+)\s*(?:\([^)]*\))?\s*(?:\([^)]+\))?\s+t(\d+))", regexFlags);
    // Catch v# registers (broad match for any dcl_input flavor)
    static std::regex inputRegex(R"(dcl_input[^\s]*\s+v(\d+))", regexFlags);
    // Catch cb# registers
    static std::regex cbRegex(R"(dcl_constantbuffer\s+cb(\d+)\[(\d+)\])", regexFlags);
    // Catch o# registers (broad match for any dcl_output flavor)
    static std::regex outputRegex(R"(dcl_output[^\s]*\s+o(\d+))", regexFlags);
    // Parse the disassembly text
    std::istringstream iss(disasmStr);
    std::string line;
    // Clear the buffers before filling them in case this is called multiple times for the same entry (e.g., if we analyze both pixel and vertex shader for the same entry)
    entry.textureSlots.clear();
    entry.textureDimensions.clear();
    entry.textureSlotMask = 0;
    entry.textureDimensionMask = 0;
    entry.inputTextureCount = 0;
    entry.inputCount = 0;
    entry.inputMask = 0;
    // Manual loop for safety to avoid sizeof pointer issues
    for (int i = 0; i < 14; ++i) entry.expectedCBSizes[i] = 0;
    entry.outputCount = 0;
    entry.outputMask = 0;
    int inputTextureCount = 0;
    int inputCount = 0;
    int outputCount = 0;
    while (std::getline(iss, line)) {
        // Look for texture declarations to detect texture slots and dimensions (mainly pixel shaders)
        // Example: "dcl_resource_texture2d (float,float,float,float) t0"
        // Dimensions from d3dcommon.h
        // D3D11_SRV_DIMENSION_UNKNOWN = 0
        // D3D11_SRV_DIMENSION_BUFFER = 1
        // D3D11_SRV_DIMENSION_TEXTURE1D = 3
        // D3D11_SRV_DIMENSION_TEXTURE2D = 4
        // D3D11_SRV_DIMENSION_TEXTURE2DMS = 6
        // D3D11_SRV_DIMENSION_TEXTURE3D = 7
        // D3D11_SRV_DIMENSION_TEXTURECUBE = 8
        // D3D11_SRV_DIMENSION_TEXTURE1DARRAY = 4
        // D3D11_SRV_DIMENSION_TEXTURE2DARRAY = 5
        // D3D11_SRV_DIMENSION_TEXTURECUBEARRAY = 11
        std::smatch match;
        // Texture / Resource declaration parsing (mainly pixel shaders)
        if (std::regex_search(line, match, texRegex)) {
            std::string texType = match[1];      // "texture1d"
            int slot = std::stoi(match[2]);      // 4
            int dimension = 0;
            if (texType == "texture2d") dimension = 4;
            else if (texType == "texture2dms") dimension = 6;
            else if (texType == "texture2darray") dimension = 5;
            else if (texType == "texturecube") dimension = 8;
            else if (texType == "texturecubearray") dimension = 11;
            else if (texType == "texture3d") dimension = 7;
            else if (texType == "texture1d") dimension = 3;
            else if (texType == "buffer") dimension = 1;
            else if (texType == "raw" || texType == "structured") dimension = 0; // For Vertex shaders
            else dimension = 0; // unknown or unsupported type
            entry.textureSlots.push_back(slot); // slot
            entry.textureDimensions.push_back({dimension, slot});
            entry.textureSlotMask |= (1u << slot);
            if (dimension < 32) {
                entry.textureDimensionMask |= (1u << dimension);
            }
            inputTextureCount++;
            continue;
        }
        // Input declaration parsing (mainly vertex shaders)
        // Look for input like POSITION or TEXCOORD to detect input count (mainly vertex shaders)
        if (std::regex_search(line, match, inputRegex)) {
            int regIndex = std::stoi(match[1].str());
            entry.inputMask |= (1u << regIndex);
            inputCount++;
            continue;
        }
        // Constant Buffer declaration parsing to detect expected CB sizes for matching (pixel and vertex shaders)
        // Example: "dcl_constantbuffer CB0[4], immediateIndexed"
        if (std::regex_search(line, match, cbRegex)) {
            int slot = std::stoi(match[1].str());
            int sizeInDwords = std::stoi(match[2].str());
            if (slot >= 0 && slot < 14) {
                entry.expectedCBSizes[slot] = sizeInDwords * 16;
            }
            continue;
        }
        // Look for output declarations to detect output count (pixel and vertex shaders)
        // Example: "dcl_output o0.xyzw"
        if (std::regex_search(line, match, outputRegex)) {
            int outputIndex = std::stoi(match[1].str());
            entry.outputMask |= (1u << outputIndex);
            outputCount++;
        }
    }
    // Clean up
    disassembly->Release();
    entry.inputTextureCount = inputTextureCount;
    entry.inputCount = inputCount;
    entry.outputCount = outputCount;
    return true;
}

// Orchestrator for hot INI reload - rematches all ShaderDB entries against current definitions
void RematchAllShaders_Internal() {
    std::unique_lock lockDB(g_ShaderDB.mutex);  // Write lock on ShaderDB
    if (DEBUGGING)
        GCollection::INFO("RematchAllShaders_Internal: Rematching {} pixel shaders and vertex shaders...", g_ShaderDB.entries.size());
    int matchedPS = 0;
    int matchedVS = 0;
    // Iterate definitions in priority order (already sorted)
    std::shared_lock lock(g_shaderDefinitions.mutex);
    for (ShaderDefinition* def : g_shaderDefinitions.definitions) {
        if (!def->active) continue;
        // Check all pixel shaders for this definition
        for (auto& [shader, entry] : g_ShaderDB.entries) {
            if (entry.IsMatched()) continue;
            if (DoesEntryMatchDefinition_Internal(entry, def)) {
                if (DEBUGGING)
                    GCollection::INFO("RematchAllShaders_Internal: Matched {} shader with hash {:08X} and size {} bytes to definition '{}'", (def->type == ShaderType::Pixel ? "pixel" : "vertex"), entry.hash, entry.size, def->id);
                entry.matchedDefinition = def;
                entry.SetMatched(true);
                if (def->type == ShaderType::Pixel) {
                    matchedPS++;
                } else if (def->type == ShaderType::Vertex) {
                    matchedVS++;
                }
                if (DEVELOPMENT && def->log) {
                    GCollection::INFO("RematchAllShaders_Internal: ------------------------------------------------");
                    GCollection::INFO("RematchAllShaders_Internal: Found matching shader definition '{}' for {} shader with hash {:08X} and size {} bytes.", def->id, entry.type == ShaderType::Vertex ? "Vertex" : "Pixel", entry.hash, entry.size);
                    GCollection::INFO(" - Shader CB Sizes: {},{},{},{},{},{},{},{},{},{},{},{},{},{}", 
                        entry.expectedCBSizes[0], entry.expectedCBSizes[1], entry.expectedCBSizes[2], entry.expectedCBSizes[3],
                        entry.expectedCBSizes[4], entry.expectedCBSizes[5], entry.expectedCBSizes[6], entry.expectedCBSizes[7],
                        entry.expectedCBSizes[8], entry.expectedCBSizes[9], entry.expectedCBSizes[10], entry.expectedCBSizes[11],
                        entry.expectedCBSizes[12], entry.expectedCBSizes[13]);
                    GCollection::INFO(" - Shader Texture Register Slots: {}", entry.textureSlots.empty() ? "None" : "");
                    for (const auto& slot : entry.textureSlots) {
                        GCollection::INFO("   - Slot: t{}", slot);
                    }
                    GCollection::INFO(" - Shader Texture Dimensions: {}", entry.textureDimensions.empty() ? "None" : "");
                    for (const auto& [dimension, slot] : entry.textureDimensions) {
                        GCollection::INFO("   - Dimension: {}, Slot: t{}", dimension, slot);
                    }
                    GCollection::INFO(" - Shader Texture Usage Bitmask: 0x{:08X}", entry.textureSlotMask);
                    GCollection::INFO(" - Shader Texture Dimension Bitmask: 0x{:08X}", entry.textureDimensionMask);
                    GCollection::INFO(" - Shader Input Texture Count: {}", entry.inputTextureCount != -1 ? std::to_string(entry.inputTextureCount) : "X");
                    GCollection::INFO(" - Shader Input Count: {}", entry.inputCount != -1 ? std::to_string(entry.inputCount) : "X");
                    GCollection::INFO(" - Shader Input Mask: 0x{:08X}", entry.inputMask);
                    GCollection::INFO(" - Shader Output Count: {}", entry.outputCount != -1 ? std::to_string(entry.outputCount) : "X");
                    GCollection::INFO(" - Shader Output Mask: 0x{:08X}", entry.outputMask);
                    GCollection::INFO("RematchAllShaders_Internal: ------------------------------------------------");
                }
                if (DEVELOPMENT && def->dump && !entry.IsDumped()) {
                    DumpOriginalShader_Internal(entry, def);
                    entry.SetDumped(true);
                }
            }
        }
    }
    if (DEBUGGING)
        GCollection::INFO("RematchAllShaders_Internal: Matched {} pixel shaders and {} vertex shaders", matchedPS, matchedVS);
}

// DEVGUI drawing function for shader debug overlay is called by the Hook Present once per frame
// Should ONLY contain ImGui drawing code!
void UIDrawShaderDebugOverlay() {
    // Position window in top-left corner
    ImGui::SetNextWindowPos(ImVec2(10, 10), ImGuiCond_Always);
    // Set width/height
    ImGui::SetNextWindowSize(ImVec2(DEVGUI_WIDTH, DEVGUI_HEIGHT), ImGuiCond_Always);
    // Make background semi-transparent
    ImGui::SetNextWindowBgAlpha(DEVGUI_OPACITY);
    // Create the Window
    ImGui::Begin("GFXBooster Shader Monitor");
    // Tickboxes for showing replaced shaders and locking the shader list
    ImGui::SameLine(ImGui::GetWindowWidth() - 240);
    ImGui::Checkbox("Replaced", &g_showReplaced);
    ImGui::SameLine(ImGui::GetWindowWidth() - 120);
    if (ImGui::Checkbox("Lock list", &g_shaderListLocked)) {
        if (g_shaderListLocked) UILockShaderList_Internal();
        else UIUnlockShaderList_Internal();
    }
    // Collapsing header for active definitions and their matched shaders
    if (ImGui::CollapsingHeader("Active Definitions", ImGuiTreeNodeFlags_DefaultOpen)) {
        ImGui::Columns(5, "shader_columns");
        // Calculate width of "Action" column based on text size of "Unflash" button plus padding
        float charW = ImGui::CalcTextSize("W").x;
        float pad   = ImGui::GetStyle().ItemSpacing.x * 2.0f;
        ImGui::SetColumnWidth(1, charW * 8.0f + pad); // Status (8 chars)
        ImGui::SetColumnWidth(2, charW * 4.0f + pad); // Used   (4 chars)
        ImGui::SetColumnWidth(3, charW * 8.0f + pad); // Hash   (8 chars)
        ImGui::SetColumnWidth(4, charW * 7.0f + pad); // Action (7 chars)
        // Fix the ID column to take up the remaining space after the fixed columns
        float totalAvail = DEVGUI_WIDTH - ImGui::GetStyle().WindowPadding.x * 2.0f;
        float fixedCols  = (charW * (8+4+8+7)) + pad * 4.0f;
        float remaining   = (std::max)(0.0f, totalAvail - fixedCols);
        ImGui::SetColumnWidth(0, remaining);
        // Headers
        ImGui::Text("ID"); ImGui::NextColumn();
        ImGui::Text("Status");        ImGui::NextColumn();
        ImGui::Text("Used");          ImGui::NextColumn();
        ImGui::Text("Hash");          ImGui::NextColumn();
        ImGui::Text("Action");        ImGui::NextColumn();
        ImGui::Separator();
        // Render a single row
        auto renderRow = [&](ShaderDBEntry& entry) {
            ShaderDefinition* def = entry.GetMatchedDefinition();
            // Column 1: ID
            const char* id = def ? def->id.c_str() : "<removed>";
            bool hasReplacement = (entry.type == ShaderType::Pixel) ? (entry.GetReplacementPixelShader() != nullptr) : (entry.GetReplacementVertexShader() != nullptr);
            bool hasShaderFile = def && !def->shaderFile.empty();
            if (hasReplacement) ImGui::TextColored(ImVec4(0,1,0,1), "%s", id);
            else if (hasShaderFile) ImGui::TextColored(ImVec4(1,0.5f,0,1), "%s", id);
            else ImGui::TextColored(ImVec4(1,1,0,1), "%s", id);
            ImGui::NextColumn();
            // Column 2: Status
            if (hasReplacement) ImGui::TextColored(ImVec4(0,1,0,1), "REPLACED");
            else if (hasShaderFile) ImGui::TextColored(ImVec4(1,0.5f,0,1), "INVALID");
            else ImGui::TextColored(ImVec4(1,1,0,1), "MATCHED");
            ImGui::NextColumn();
            // Column 3: Recently used
            bool usedThisFrame = entry.IsRecentlyUsed();
            if (usedThisFrame) {
                ImGui::TextColored(ImVec4(0,1,1,1), "YES");
                entry.SetRecentlyUsed(false); // reset for next frame
            } else {
                ImGui::TextColored(ImVec4(0.5f,0.5f,0.5f,1), "NO");
            }
            ImGui::NextColumn();
            // Column 4: Hash
            ImGui::Text("%08X", entry.hash);
            ImGui::NextColumn();
            // Column 5: Actions (Flash)
            ImGui::PushID((void*)entry.originalShader); // Use original shader pointer as unique ID to avoid ID collisions in the list
            auto* currentShader = entry.GetReplacementPixelShader();
            if (entry.type == ShaderType::Pixel && (!currentShader || currentShader == g_flashPixelShader)) {
                ImGui::BeginDisabled(!g_flashPixelShader && currentShader != g_flashPixelShader);
                if (ImGui::SmallButton(currentShader == g_flashPixelShader ? "Unflash" : "Flash")) {
                    entry.SetReplacementPixelShader(currentShader == g_flashPixelShader ? nullptr : g_flashPixelShader);
                }
                ImGui::EndDisabled();
            }
            ImGui::PopID();
            ImGui::NextColumn();
        };
        // Iterate either snapshot (locked) or live DB (unlocked)
        {
            std::shared_lock lock(g_ShaderDB.mutex);
            if (g_shaderListLocked) {
                for (void* key : g_lockedShaderKeys) {
                    auto it = g_ShaderDB.entries.find(key);
                    if (it == g_ShaderDB.entries.end()) {
                        // Skip if the entry no longer exists in the ShaderDB
                        continue;
                    }
                    // Filter out replaced shaders if the option is disabled
                    if (!g_showReplaced) {
                        bool isReplacedNonFlash =
                            (it->second.type == ShaderType::Pixel && it->second.GetReplacementPixelShader() && it->second.GetReplacementPixelShader() != g_flashPixelShader)
                        || (it->second.type == ShaderType::Vertex && it->second.GetReplacementVertexShader());
                        if (isReplacedNonFlash)
                            continue;
                    }
                    renderRow(it->second);
                }
            } else {
                for (auto& [ptr, entry] : g_ShaderDB.entries) {
                    ShaderDefinition* def = entry.GetMatchedDefinition();
                    if (entry.IsMatched() && entry.IsRecentlyUsed() && def) {
                        // Filter out replaced shaders if the option is disabled
                        if (!g_showReplaced) {
                            bool isReplacedNonFlash =
                                (entry.type == ShaderType::Pixel && entry.GetReplacementPixelShader() && entry.GetReplacementPixelShader() != g_flashPixelShader)
                            || (entry.type == ShaderType::Vertex && entry.GetReplacementVertexShader());
                            if (isReplacedNonFlash)
                                continue;
                        }
                        renderRow(entry);
                    }
                }
            }
        }
        ImGui::Columns(1);
    }
    ImGui::End();
}
// Lock the current shader list in the UI to prevent it from changing
void UILockShaderList_Internal() {
    std::shared_lock lock(g_ShaderDB.mutex);
    g_lockedShaderKeys.clear();
    g_lockedShaderKeys.reserve(g_ShaderDB.entries.size());
    for (auto& [shaderKey, entry] : g_ShaderDB.entries) {
        ShaderDefinition* def = entry.GetMatchedDefinition();
        // Snapshot **what is currently visible** in the UI
        if (entry.IsMatched() && entry.IsRecentlyUsed() && def) {
            g_lockedShaderKeys.push_back(shaderKey);
        }
    }
}
void UIUnlockShaderList_Internal() {
    g_shaderListLocked = false;
    g_lockedShaderKeys.clear();
}

// Update the custom buffer for shaders
void UpdateCustomBuffer_Internal() {
    // Fill the custom buffer data structure with current frame info
    static LARGE_INTEGER frequency = {};
    static LARGE_INTEGER lastFrameTime = {};
    static LARGE_INTEGER startTime = {};
    static bool initialized = false;
    static bool firstFrame = true; // Flag to initialize smoothedFPS properly
    static float smoothedFPS = 0.0f;
    static uint32_t frameCounter = 0;
    // Initialize timing on first call
    if (!initialized) {
        QueryPerformanceFrequency(&frequency);
        QueryPerformanceCounter(&startTime);
        lastFrameTime = startTime;
        initialized = true;
    }
    // Get current time
    LARGE_INTEGER currentTime;
    QueryPerformanceCounter(&currentTime);
    // Calculate delta time
    float deltaTime = static_cast<float>(currentTime.QuadPart - lastFrameTime.QuadPart) / static_cast<float>(frequency.QuadPart);
    lastFrameTime = currentTime;
    // Calculate total elapsed time
    float totalTime = static_cast<float>(currentTime.QuadPart - startTime.QuadPart) / static_cast<float>(frequency.QuadPart);
    // Calculate Instant FPS
    // We use a small epsilon (0.0001) to prevent any potential division by zero
    float instantFPS = (deltaTime > 0.0001f) ? (1.0f / deltaTime) : 0.0f;
    // Smooth the FPS
    if (firstFrame && instantFPS > 0.0f) {
        // Use Instant FPS at start
        smoothedFPS = instantFPS;
        firstFrame = false;
    } else {
        // Standard exponential moving average for all subsequent frames
        smoothedFPS = smoothedFPS * 0.95f + instantFPS * 0.05f;
    }
    // Get screen resolution / camera state
    // NOTE: The original implementation queried internal renderer/camera structures which can vary across
    // CommonLibF4 versions. To keep the plugin building across variants, we provide conservative defaults
    // and rely on the shader-side to tolerate zeros.
    float resX = 1920.0f;
    float resY = 1080.0f;

    // Mouse position (screen-space)
    POINT cursorPos{};
    ::GetCursorPos(&cursorPos);
    float mousePosX = static_cast<float>(cursorPos.x);
    float mousePosY = static_cast<float>(cursorPos.y);

    // Camera (world-space) + angles
    float camX = 0.0f;
    float camY = 0.0f;
    float camZ = 0.0f;
    float yaw = 0.0f;
    float pitch = 0.0f;

    // Game-state approximations
    float fov = 90.0f;
    float healthPerc = 1.0f;
    float radiationDmg = 0.0f;
    float windSpeed = 0.0f;

    // Resolve depth SRV if available (optional).
    ID3D11ShaderResourceView* depthSRV = nullptr;
    if (g_rendererData) {
        auto& dst = detail::GetDepthStencilArray(g_rendererData);
        if (!dst.empty() && dst[0].srViewDepth) {
            depthSRV = dst[0].srViewDepth;
        }
    }

    // Fill the custom buffer data structure
    g_customBufferData.time     = totalTime;
    g_customBufferData.delta    = deltaTime;
    g_customBufferData.dayCycle = fmodf(totalTime, 86400.0f) / 86400.0f;
    g_customBufferData.frame    = static_cast<float>(frameCounter++);
    g_customBufferData.fps      = smoothedFPS;
    g_customBufferData.resX     = resX;
    g_customBufferData.resY     = resY;
    g_customBufferData.mouseX   = static_cast<float>(mousePos.x) / resX;
    g_customBufferData.mouseY   = static_cast<float>(mousePos.y) / resY;
    g_customBufferData.windSpeed = g_windSpeed;
    g_customBufferData.windAngle = g_windAngle;
    g_customBufferData.windTurb  = g_windTurbulence;
    g_customBufferData.vpLeft   = vp.left;
    g_customBufferData.vpTop    = vp.top;
    g_customBufferData.vpWidth  = vp.right - vp.left;
    g_customBufferData.vpHeight = vp.bottom - vp.top;
    g_customBufferData.camX     = camX;
    g_customBufferData.camY     = camY;
    g_customBufferData.camZ     = camZ;
    g_customBufferData.pRadDmg  = g_radDmg;
    g_customBufferData.viewDirX = vd.m128_f32[0];
    g_customBufferData.viewDirY = vd.m128_f32[1];
    g_customBufferData.viewDirZ = vd.m128_f32[2];
    g_customBufferData.pHealthPerc = g_healthPerc;
    DirectX::XMStoreFloat4(&g_customBufferData.g_InvProjRow0, invProj.r[0]);
    DirectX::XMStoreFloat4(&g_customBufferData.g_InvProjRow1, invProj.r[1]);
    DirectX::XMStoreFloat4(&g_customBufferData.g_InvProjRow2, invProj.r[2]);
    DirectX::XMStoreFloat4(&g_customBufferData.g_InvProjRow3, invProj.r[3]);
    g_customBufferData.random  = randomValue;
    // Create or update our custom buffer resource view with the new data
    if (!g_rendererData) {
        g_rendererData = RE::GetRendererData();
        if (!g_rendererData) {
            REX::WARN("UpdateCustomBuffer_Internal: Cannot update custom buffer: renderer data not ready");
            return;
        }
    }
    auto* device = detail::GetDXDevice();
    if (!g_customSRVBuffer && device) {
        REX::W32::D3D11_BUFFER_DESC desc{};
        desc.Usage            = REX::W32::D3D11_USAGE_DYNAMIC;
        desc.ByteWidth        = sizeof(GFXBoosterAccessData);
        desc.BindFlags        = REX::W32::D3D11_BIND_SHADER_RESOURCE;
        desc.CPUAccessFlags   = REX::W32::D3D11_CPU_ACCESS_WRITE;
        desc.MiscFlags        = REX::W32::D3D11_RESOURCE_MISC_BUFFER_STRUCTURED;
        desc.StructureByteStride = sizeof(GFXBoosterAccessData);
        REX::W32::ID3D11Buffer* tmpBuf = nullptr;
        HRESULT hr = device->CreateBuffer(&desc, nullptr, &tmpBuf);
        g_customSRVBuffer = AsRawD3D11Buffer(tmpBuf);
        if (FAILED(hr)) {
            REX::WARN("UpdateCustomBuffer_Internal: Failed to create custom buffer. HRESULT: 0x{:08X}", hr);
            return;
        }
    }
    if (g_customSRVBuffer && !g_customSRV && device) {
        REX::W32::D3D11_SHADER_RESOURCE_VIEW_DESC srvDesc{};
        srvDesc.Format                    = REX::W32::DXGI_FORMAT_UNKNOWN;
        srvDesc.ViewDimension             = REX::W32::D3D11_SRV_DIMENSION_BUFFER;
        srvDesc.Buffer.firstElement       = 0;
        srvDesc.Buffer.numElements        = 1;
        //srvDesc.Buffer.elementWidth       = sizeof(GFXBoosterAccessData); // needs to be commented or the SRV creation fails!!!111
        REX::W32::ID3D11ShaderResourceView* tmpSRV = nullptr;
        HRESULT hr = device->CreateShaderResourceView(AsRexD3D11Buffer(g_customSRVBuffer), &srvDesc, &tmpSRV);
        g_customSRV = AsRawD3D11SRV(tmpSRV);
        if (FAILED(hr)) {
            REX::WARN("UpdateCustomBuffer_Internal: Failed to create custom SRV. HRESULT: 0x{:08X}", hr);
            return;
        }
    }
    auto* context = detail::GetDXContext();
    if (g_customSRVBuffer && context) {
        REX::W32::D3D11_MAPPED_SUBRESOURCE m;
        context->Map(AsRexD3D11Buffer(g_customSRVBuffer),0,REX::W32::D3D11_MAP_WRITE_DISCARD,0,&m);
        memcpy(m.data, &g_customBufferData, sizeof(g_customBufferData));
        context->Unmap(AsRexD3D11Buffer(g_customSRVBuffer),0);
    }
}

// This is called at GameData ready to set up our hooks on the graphics device and context
bool InstallGFXHooks_Internal() {
    if (!g_rendererData) {
        g_rendererData = RE::GetRendererData();
    }
    if (!g_rendererData || !detail::GetDXDevice()) {
        REX::WARN("InstallGFXHooks_Internal: Cannot install hook: device not ready");
        return false;
    }
    REX::W32::ID3D11Device* device = detail::GetDXDevice();
    REX::W32::ID3D11DeviceContext* context = detail::GetDXContext();
    DWORD oldProtect;
    auto* ctx = detail::GetDXContext();
    auto* contextVTable = *reinterpret_cast<void***>(ctx);
    // Hook ID3D11DeviceContext::PSSetShader (vtable index 9)
    OriginalPSSetShader = reinterpret_cast<PSSetShader_t>(contextVTable[9]);
    if (!VirtualProtect(&contextVTable[9], sizeof(void*), PAGE_EXECUTE_READWRITE, &oldProtect)) {
        REX::WARN("InstallGFXHooks_Internal: VirtualProtect failed for PSSetShader");
        return false;
    }
    contextVTable[9] = reinterpret_cast<void*>(MyPSSetShader);
    VirtualProtect(&contextVTable[9], sizeof(void*), oldProtect, &oldProtect);
    GCollection::INFO("InstallGFXHooks_Internal: PSSetShader hook installed");
    // Hook ID3D11DeviceContext::VSSetShader (vtable index 11)
    OriginalVSSetShader = reinterpret_cast<VSSetShader_t>(contextVTable[11]);
    if (!VirtualProtect(&contextVTable[11], sizeof(void*), PAGE_EXECUTE_READWRITE, &oldProtect)) {
        REX::WARN("InstallGFXHooks_Internal: VirtualProtect failed for VSSetShader");
        return false;
    }
    contextVTable[11] = reinterpret_cast<void*>(MyVSSetShader);
    VirtualProtect(&contextVTable[11], sizeof(void*), oldProtect, &oldProtect);
    GCollection::INFO("InstallGFXHooks_Internal: VSSetShader hook installed");
    // Hook IDXGISwapChain::Present (vtable index 8)
    auto* swapChain = detail::GetRenderWindowArray(g_rendererData)[0].swapChain;  // First window (main)
    auto* swapChainVTable = *reinterpret_cast<void***>(swapChain);
    OriginalPresent = reinterpret_cast<Present_t>(swapChainVTable[8]);
    if (!VirtualProtect(&swapChainVTable[8], sizeof(void*), PAGE_EXECUTE_READWRITE, &oldProtect)) {
        REX::WARN("InstallGFXHooks_Internal: VirtualProtect failed for Present");
        return false;
    }
    swapChainVTable[8] = reinterpret_cast<void*>(MyPresent);
    VirtualProtect(&swapChainVTable[8], sizeof(void*), oldProtect, &oldProtect);
    GCollection::INFO("InstallGFXHooks_Internal: Present hook installed");
    GCollection::INFO("InstallGFXHooks_Internal: All Hooks installed successfully");
    // Set up ImGui if DEVGUI_ON=true
    if (!DEVGUI_ON) {
        GCollection::INFO("InstallGFXHooks_Internal: DEVGUI_ON is false, skipping ImGui initialization");
        return true;
    }
    // All Hooks installed successfully
    // Compile the flash shader used for highlighting matched shaders in the dev GUI
    if (flashPixelShaderHLSL) {
        ID3DBlob* blob = nullptr;
        ID3DBlob* err  = nullptr;
        HRESULT hr = D3DCompile(
            flashPixelShaderHLSL,
            strlen(flashPixelShaderHLSL),
            "flash_ps",
            nullptr,
            nullptr,
            "main", "ps_5_0",
            D3DCOMPILE_OPTIMIZATION_LEVEL3,
            0,
            &blob,
            &err
        );
        if (!REX::W32::SUCCESS(hr)) {
            if (err)
                REX::WARN("Flash shader compile error: {}", static_cast<const char*>(err->GetBufferPointer())); err->Release();
            // return false; // Still continue, it is not essential
        }
        if (blob) {
            g_isCreatingReplacementShader = true;
            hr = detail::GetDXDevice()->CreatePixelShader(
                blob->GetBufferPointer(),
                blob->GetBufferSize(),
                nullptr,
                &g_flashPixelShader
            );
            g_isCreatingReplacementShader = false;
            blob->Release();
            if (!REX::W32::SUCCESS(hr))
                REX::WARN("CreatePixelShader failed for flash shader with HRESULT: 0x{:08X}", hr);
            // return false; // Still continue, it is not essential
        }
    }
    GCollection::INFO("About to initialize ImGui...");
    IMGUI_CHECKVERSION();
    ImGui::CreateContext();
    // Disable saving to imgui.ini to use the INI settings
    ImGuiIO& io = ImGui::GetIO();
    io.IniFilename = nullptr;
    HWND hwnd = FindWindowA("Fallout4", nullptr);
    g_originalWndProc = (WNDPROC)SetWindowLongPtr(hwnd, GWLP_WNDPROC, (LONG_PTR)ImGuiWndProc);
    GCollection::INFO("HWND: {}", (void*)hwnd);
    if (!hwnd) {
        REX::WARN("Failed to get game window handle");
        return false;
    }
    ImGui_ImplWin32_Init(hwnd);
    ImGui_ImplDX11_Init(
        reinterpret_cast<ID3D11Device*>(detail::GetDXDevice()),
        reinterpret_cast<ID3D11DeviceContext*>(detail::GetDXContext())
    );
    GCollection::INFO("DX11 ImGui initialized");
    g_imguiInitialized = true;
    return true;
}

// This is called during Plugin load very early
bool InstallShaderCreationHooks_Internal() {
    if (!g_rendererData || !detail::GetDXDevice()) {
        g_rendererData = RE::GetRendererData();
    }
    if (!g_rendererData || !detail::GetDXDevice()) {
        return false;
    }
    auto* dev = detail::GetDXDevice();
    auto* deviceVTable = *reinterpret_cast<void***>(dev);
    DWORD oldProtect;
    // Hook ID3D11Device::CreatePixelShader (vtable index 15)
    OriginalCreatePixelShader = reinterpret_cast<CreatePixelShader_t>(deviceVTable[15]);
    VirtualProtect(&deviceVTable[15], sizeof(void*), PAGE_EXECUTE_READWRITE, &oldProtect);
    deviceVTable[15] = reinterpret_cast<void*>(MyCreatePixelShader);
    VirtualProtect(&deviceVTable[15], sizeof(void*), oldProtect, &oldProtect);
    GCollection::INFO("InstallShaderCreationHooks_Internal: CreatePixelShader hook installed");
    // Hook ID3D11Device::CreateVertexShader (vtable index 12)
    OriginalCreateVertexShader = reinterpret_cast<CreateVertexShader_t>(deviceVTable[12]);
    VirtualProtect(&deviceVTable[12], sizeof(void*), PAGE_EXECUTE_READWRITE, &oldProtect);
    deviceVTable[12] = reinterpret_cast<void*>(MyCreateVertexShader);
    VirtualProtect(&deviceVTable[12], sizeof(void*), oldProtect, &oldProtect);
    GCollection::INFO("InstallShaderCreationHooks_Internal: CreateVertexShader hook installed");
    return true;
}
