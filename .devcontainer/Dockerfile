# FROM quantconnect/lean:foundation-arm64
# FROM quantconnect/lean:foundation
FROM cqxmzz/lean:foundation

# from dockerfile of latest
RUN pip install --no-cache-dir ptvsd==4.3.2 debugpy pydevd-pycharm~=201.8538.36
# Install vsdbg for remote C# debugging in Visual Studio and Visual Studio Code
RUN wget https://aka.ms/getvsdbgsh -O - 2>/dev/null | /bin/sh /dev/stdin -v 16.9.20122.2 -l /root/vsdbg

# for pyscipopt, conda doesn't support arm64
# conda install -c conda-forge pyscipopt
RUN if [ "$(uname -m)" = "aarch64" ] || [ "$(uname -m)" = "arm64" ]; then \
    echo "deb http://ports.ubuntu.com/ubuntu-ports jammy main universe" >> /etc/apt/sources.list \
    && apt-get update && apt-get install -y wget cmake g++ m4 xz-utils libgmp-dev unzip zlib1g-dev libboost-program-options-dev libboost-serialization-dev libboost-regex-dev libboost-iostreams-dev libtbb-dev libreadline-dev pkg-config git liblapack-dev libgsl-dev flex bison libcliquer-dev gfortran file dpkg-dev libopenblas-dev rpm \
    && wget -P /scip https://eu.mirror.archlinuxarm.org/aarch64/community/scip-8.0.3-2-aarch64.pkg.tar.xz \
    && tar -xf /scip/scip-8.0.3-2-aarch64.pkg.tar.xz -C / \
    && rm -r /scip \
    && pip install --no-cache-dir pyscipopt; \
    else \
    conda install -c conda-forge pyscipopt \
    && add-apt-repository -y ppa:ubuntu-toolchain-r/test \
    && apt-get update \
    && apt-get upgrade -y libstdc++6; \
    fi;

# Install dos2unix utility for converting pesky windows formatting when needed
# RUN apt-get update && apt-get install -y dos2unix

# Install QuantConnect Stubs for Python Autocomplete
RUN pip install --no-cache-dir quantconnect-stubs

RUN pip install --no-cache-dir xgboost

RUN pip install --upgrade cvxpy

# Can override with '-w'
WORKDIR /Lean/Launcher/bin/Release

ENTRYPOINT [ "dotnet", "QuantConnect.Lean.Launcher.dll" ]