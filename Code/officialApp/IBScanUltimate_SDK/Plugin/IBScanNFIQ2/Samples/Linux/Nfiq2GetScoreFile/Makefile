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
CXXSTD = -std=c++11

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
    $(CXXSTD) \
    -fPIC \
    $(INCLUDES)

LDFLAGS = \
    -L ../../../../../lib \
    -Wl,--no-as-needed

LIBRARIES = \
    -lIBScanNFIQ2

################################################################################
# TARGETS
################################################################################

BINDIR = bin/$(ARCHABI)
OBJDIR = obj/$(ARCHABI)

SRCS = nfiq2getscore.cpp
OBJS = $(OBJDIR)/nfiq2getscore.o

################################################################################
# RULES
################################################################################

.PHONY: all clean

all: $(BINDIR)/nfiq2getscore

$(OBJDIR):
	mkdir -p $(OBJDIR)

$(BINDIR):
	mkdir -p $(BINDIR)

$(OBJDIR)/nfiq2getscore.o: nfiq2getscore.cpp | $(OBJDIR)
	$(CXX) $(CXXFLAGS) -c -o $@ $<

$(BINDIR)/nfiq2getscore: $(OBJS) | $(BINDIR)
	$(CXX) -o $@ $^ $(LDFLAGS) $(LIBRARIES)

clean:
	rm -rf obj bin
