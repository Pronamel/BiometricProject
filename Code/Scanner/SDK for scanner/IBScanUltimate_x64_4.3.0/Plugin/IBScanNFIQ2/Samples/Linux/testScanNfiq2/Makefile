################################################################################
# IBScanUltimate NFIQ2 Test (Linux x86_64)
################################################################################

################################################################################
# REQUIRED INPUT
################################################################################
# Usage:
#   make -f Makefile_x86_64.mk
################################################################################

################################################################################
# ARCH / PLATFORM (STATIC)
################################################################################

ARCHABI    = Linux_x64
ARCHFLAGS  = -m64

################################################################################
# COMPILER / FLAGS
################################################################################

CXX          = g++
OPTIMIZATION = -O2
WARNINGS     = -Wall

INCLUDES = \
    -I ../../../../../include \
    -I ../../../include

DEFINES = \
    -DBSD \
    -D__linux__ \
    -D_MULTI_THREADED

CXXFLAGS = \
    $(OPTIMIZATION) \
    $(WARNINGS) \
    $(DEFINES) \
    $(ARCHFLAGS) \
    -fPIC \
    $(INCLUDES)

LDFLAGS = \
    -L ../../../../../lib/$(ARCHABI) \
    -L ../../../IBScanNFIQ2_Libs/Linux_x64 \
    -Wl,--no-as-needed

LIBRARIES = \
    -lstdc++ \
    -lpthread \
    -lAKXUS \
    -lusb-1.0 \
    -luvc \
    -lIBScanUltimate \
    -lIBScanNFIQ2

################################################################################
# TARGETS
################################################################################

BINDIR = bin/$(ARCHABI)
OBJDIR = obj/$(ARCHABI)

SRCS = testScanUNfiq2.cpp
OBJS = $(OBJDIR)/testScanUNfiq2.o

################################################################################
# RULES
################################################################################

.PHONY: all clean

all: $(BINDIR)/testScanUNfiq2

$(OBJDIR):
	mkdir -p $(OBJDIR)

$(BINDIR):
	mkdir -p $(BINDIR)

$(OBJDIR)/testScanUNfiq2.o: testScanUNfiq2.cpp | $(OBJDIR)
	$(CXX) $(CXXFLAGS) -c -o $@ $<

$(BINDIR)/testScanUNfiq2: $(OBJS) | $(BINDIR)
	$(CXX) -o $@ $^ $(LDFLAGS) $(LIBRARIES)

clean:
	rm -rf obj bin
