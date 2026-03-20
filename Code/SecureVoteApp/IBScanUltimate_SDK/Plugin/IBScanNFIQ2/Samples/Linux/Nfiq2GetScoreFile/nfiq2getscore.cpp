
// Samples/Linux/Nfiq2GetScoreFile/main.cpp
#include <iostream>
#include <fstream>
#include <vector>
#include <cstring>
#include <cstdlib>

// Exported wrapper API
#include "LinuxPort.h"
#include "IBScanNFIQ2Api.h"

#pragma pack(push, 1)
struct BMPHeader {
    uint16_t bfType;
    uint32_t bfSize;
    uint16_t bfReserved1;
    uint16_t bfReserved2;
    uint32_t bfOffBits;
};

struct BMPInfoHeader {
    uint32_t biSize;
    int32_t  biWidth;
    int32_t  biHeight;
    uint16_t biPlanes;
    uint16_t biBitCount;
    uint32_t biCompression;
    uint32_t biSizeImage;
    int32_t  biXPelsPerMeter;
    int32_t  biYPelsPerMeter;
    uint32_t biClrUsed;
    uint32_t biClrImportant;
};
#pragma pack(pop)

// Load BMP → grayscale buffer (only supports 8-bit BMP)
bool LoadBMP(const std::string& filepath,
             std::vector<unsigned char>& outBuffer,
             int& width, int& height, int& bitsPerPixel)
{
    std::ifstream file(filepath, std::ios::binary);
    if (!file.is_open()) {
        std::cerr << "[ERROR] Cannot open BMP file: " << filepath << std::endl;
        return false;
    }

    BMPHeader header{};
    BMPInfoHeader info{};

    file.read(reinterpret_cast<char*>(&header), sizeof(header));
    file.read(reinterpret_cast<char*>(&info), sizeof(info));

    if (header.bfType != 0x4D42) { // 'BM'
        std::cerr << "[ERROR] Not a BMP file" << std::endl;
        return false;
    }

    width = info.biWidth;
    height = std::abs(info.biHeight);
    bitsPerPixel = info.biBitCount;

    if (bitsPerPixel != 8) {
        std::cerr << "[ERROR] Not support " << bitsPerPixel << "bit BMP file" << std::endl;
        return false;
    }

    // 8-bit indexed BMP: read pixel data (with row padding)
    file.seekg(header.bfOffBits, std::ios::beg);
    int rowSize = ((bitsPerPixel * width + 31) / 32) * 4; // padded to 4 bytes
    std::vector<unsigned char> bmpData(rowSize * height);
    file.read(reinterpret_cast<char*>(bmpData.data()), bmpData.size());

    // Copy only pixel bytes per row (first 'width' bytes)
    outBuffer.resize(width * height);
    for (int y = 0; y < height; ++y) {
        std::memcpy(outBuffer.data() + y * width, bmpData.data() + rowSize * y, width);
    }

    // Vertical flip (BMP typically bottom-up)
    std::vector<unsigned char> flipped(width * height);
    for (int y = 0; y < height; ++y) {
        std::memcpy(flipped.data() + y * width, outBuffer.data() + (height - 1 - y) * width, width);
    }
    outBuffer.swap(flipped);

    return true;
}

int main(int argc, char* argv[])
{
    if (argc < 2) {
        std::cout << "IBScanNFIQ2 Linux CLI Sample\n";
        std::cout << "Usage: " << argv[0] << " <image.bmp>\n";
        return 0;
    }

    const std::string bmpFile = argv[1];

    char buf[256] = {0};
    IBSU_NFIQ2_GetVersion(buf);
    std::cout << "Wrapper Version: " << buf << "\n";

    std::memset(buf, 0, sizeof(buf));
    IBSU_NFIQ2_GetNISTVersion(buf);
    std::cout << "NIST/FRFXLL Version: " << buf << "\n";

    int rc = IBSU_NFIQ2_Initialize();
    if (rc != 0) {
        std::cerr << "[ERROR] IBSU_NFIQ2_Initialize() failed. rc=" << rc << "\n";
        return -1;
    }

    int width = 0, height = 0, bpp = 0;
    std::vector<unsigned char> img;

    if (!LoadBMP(bmpFile, img, width, height, bpp)) {
        return -1;
    }

    std::cout << "Loaded BMP: " << bmpFile
              << " (" << width << "x" << height
              << ", " << bpp << " bpp)\n";

    int score = -1;
    rc = IBSU_NFIQ2_ComputeScore(img.data(), width, height, bpp, &score);

    if (rc != 0) {
        std::cerr << "[ERROR] IBSU_NFIQ2_ComputeScore() failed. rc=" << rc << "\n";
        return -1;
    }

    std::cout << "NFIQ2 Score = " << score << "\n";
    return 0;
}
