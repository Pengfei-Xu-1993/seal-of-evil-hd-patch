// Generated from config/resolutions.json. Do not edit by hand.
#pragma once

namespace seal_resolution {
struct ResolutionProfile { unsigned width, height; const char *label; const char *aspect; };
static const ResolutionProfile kResolutionProfiles[] = {
    { 800, 600, "800*600", "4:3" }, // Stable ID 0
    { 1024, 768, "1024*768", "4:3" }, // Stable ID 1
    { 1920, 1080, "1920*1080", "16:9" }, // Stable ID 2
    { 2560, 1440, "2560*1440", "16:9" }, // Stable ID 3
    { 3840, 2160, "3840*2160", "16:9" }, // Stable ID 4
    { 1280, 720, "1280*720", "16:9" }, // Stable ID 5
    { 1360, 768, "1360*768", "16:9" }, // Stable ID 6
    { 1366, 768, "1366*768", "16:9" }, // Stable ID 7
    { 1536, 864, "1536*864", "16:9" }, // Stable ID 8
    { 1600, 900, "1600*900", "16:9" }, // Stable ID 9
    { 1280, 800, "1280*800", "16:10" }, // Stable ID 10
    { 1440, 900, "1440*900", "16:10" }, // Stable ID 11
    { 1600, 1000, "1600*1000", "16:10" }, // Stable ID 12
    { 1680, 1050, "1680*1050", "16:10" }, // Stable ID 13
    { 1920, 1200, "1920*1200", "16:10" }, // Stable ID 14
    { 2560, 1600, "2560*1600", "16:10" }, // Stable ID 15
    { 2880, 1800, "2880*1800", "16:10" }, // Stable ID 16
    { 3840, 2400, "3840*2400", "16:10" }, // Stable ID 17
};
static const int kResolutionOrder[] = { 0, 1, 5, 10, 6, 7, 11, 8, 9, 12, 13, 2, 14, 3, 15, 16, 4, 17 };
static const unsigned kResolutionCount = sizeof(kResolutionProfiles) / sizeof(kResolutionProfiles[0]);
static const int kDefaultResolutionId = 2;
} // namespace seal_resolution
