#include <cmath>
#include <iostream>
#include "BlurFilter.h"

#define MIN_KERNEL_SIZE 3
#define MAX_KERNEL_SIZE 9
#define IMG_SIZE_TO_KERNEL_RATIO 50
#define MAX_SIGMA 10.0f

BlurFilter::BlurFilter(int imageSize, float blurFactor) {
    
    m_KernelSize = std::max(MIN_KERNEL_SIZE, std::min(imageSize / IMG_SIZE_TO_KERNEL_RATIO, MAX_KERNEL_SIZE));
    m_Kernel = new float[m_KernelSize * m_KernelSize];
    m_HalfKernelSize = m_KernelSize >> 1;
            
            
    float sigma = std::max(1.0f, blurFactor * MAX_SIGMA);
    float r, s = 2.0f * sigma * sigma;
    float sum = 0.0f;

    // distribution function to calculate discrete kernel approximation
    for (int x = -m_HalfKernelSize; x <= m_HalfKernelSize; ++x) {
        for (int y = -m_HalfKernelSize; y <= m_HalfKernelSize; ++y) {
            
            r = sqrt(x * x + y * y);
            m_Kernel[(x + m_HalfKernelSize) * m_KernelSize + (y + m_HalfKernelSize)] = (exp(-(r * r) / s)) / (M_PI * s);
            sum += m_Kernel[(x + m_HalfKernelSize) * m_KernelSize + (y + m_HalfKernelSize)];
        }
    }

    // normalising the kernel
    for (int i = 0; i < m_KernelSize; ++i) {
        for (int j = 0; j < m_KernelSize; ++j) {
            m_Kernel[i * m_KernelSize + j] /= sum;
        }
    }
}


void BlurFilter::BlurImage(Image& originalImage, Image& targetImage) {
    
    auto originalData = originalImage.GetPixelData();
    auto targetBuffer = targetImage.GetPixelData();

    int Width = originalImage.GetWidth();
    int Height = originalImage.GetHeight();
    int bytesPerPix = originalImage.GetBytesPerPixel();
    
    float red = 0.0f, green = 0.0f, blue = 0.0f;
    int centralKernelOffset = (m_KernelSize / 2) * Width + (m_KernelSize / 2);

    std::cout << "Blurring "  << " ..." << std::endl;
    
    int pixelOffset, kernelToPixelOffset;
    for(int rowImageIndex = 0; rowImageIndex < Height; ++rowImageIndex) {
        for(int colImageIndex = 0; colImageIndex < Width; ++colImageIndex) {

            pixelOffset = rowImageIndex * Width + colImageIndex;

            for (int kernelIndex = 0; kernelIndex < m_KernelSize * m_KernelSize; ++kernelIndex) {
                
                // copy edge pixels directly
                if (rowImageIndex < m_HalfKernelSize || rowImageIndex > Height - m_HalfKernelSize ||
                    colImageIndex < m_HalfKernelSize || colImageIndex > Width - m_HalfKernelSize) {
                    blue  = originalData[bytesPerPix * pixelOffset    ];
                    green = originalData[bytesPerPix * pixelOffset + 1];
                    red   = originalData[bytesPerPix * pixelOffset + 2];
                    break;
                }
                
                // neighbour pixel coordinate corresponding to the current kernel coordinate
                kernelToPixelOffset = kernelIndex / m_KernelSize * Width + kernelIndex % m_KernelSize - centralKernelOffset;

                blue  += originalData[bytesPerPix * (pixelOffset + kernelToPixelOffset)    ] * m_Kernel[kernelIndex];
                green += originalData[bytesPerPix * (pixelOffset + kernelToPixelOffset) + 1] * m_Kernel[kernelIndex];
                red   += originalData[bytesPerPix * (pixelOffset + kernelToPixelOffset) + 2] * m_Kernel[kernelIndex];
            }

            targetBuffer[bytesPerPix * pixelOffset    ] = static_cast<std::uint8_t>(std::max(0.0f, std::min(blue, 255.0f)));
            targetBuffer[bytesPerPix * pixelOffset + 1] = static_cast<std::uint8_t>(std::max(0.0f, std::min(green, 255.0f)));
            targetBuffer[bytesPerPix * pixelOffset + 2] = static_cast<std::uint8_t>(std::max(0.0f, std::min(red, 255.0f)));
            
            // copy alpha channel
            if (bytesPerPix == 4) {
                targetBuffer[bytesPerPix * pixelOffset + 3] = originalData[bytesPerPix * pixelOffset + 3];
            }

            red = green = blue = 0.0f;
        }
    }
    std::cout << "Done." << std::endl;
}

BlurFilter::~BlurFilter() {
    delete [] m_Kernel;
}

