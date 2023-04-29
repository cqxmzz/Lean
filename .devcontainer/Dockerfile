# Use QuantConnect Research as the base
FROM quantconnect/lean:foundation-arm64

# from dockerfile of latest
RUN pip install --no-cache-dir ptvsd==4.3.2 debugpy~=1.5.1 pydevd-pycharm~=201.8538.36

# Install vsdbg for remote C# debugging in Visual Studio and Visual Studio Code
RUN wget https://aka.ms/getvsdbgsh -O - 2>/dev/null | /bin/sh /dev/stdin -v 16.9.20122.2 -l /root/vsdbg

# Install dos2unix utility for converting pesky windows formatting when needed
RUN apt-get update && apt-get install -y dos2unix

# Install QuantConnect Stubs for Python Autocomplete
RUN pip install --no-cache-dir quantconnect-stubs

RUN pip install --no-cache-dir xgboost

RUN echo "deb http://ports.ubuntu.com/ubuntu-ports/ jammy main universe" >> /etc/apt/sources.list

RUN apt-get update && apt-get install -y wget cmake g++ m4 xz-utils libgmp-dev unzip zlib1g-dev libboost-program-options-dev libboost-serialization-dev libboost-regex-dev libboost-iostreams-dev libtbb-dev libreadline-dev pkg-config git liblapack-dev libgsl-dev flex bison libcliquer-dev gfortran file dpkg-dev libopenblas-dev rpm

RUN wget -P /scip https://eu.mirror.archlinuxarm.org/aarch64/community/scip-8.0.3-2-aarch64.pkg.tar.xz

RUN tar -xf /scip/scip-8.0.3-2-aarch64.pkg.tar.xz -C /

RUN rm -r /scip

RUN pip install --no-cache-dir pyscipopt

# Can override with '-w'
WORKDIR /Lean/Launcher/bin/Release

ENTRYPOINT [ "dotnet", "QuantConnect.Lean.Launcher.dll" ]