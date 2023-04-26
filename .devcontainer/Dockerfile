# Use QuantConnect Research as the base
FROM --platform=linux/amd64 quantconnect/research:latest

# Install dos2unix utility for converting pesky windows formatting when needed
RUN apt-get update && apt-get install -y dos2unix

# Install QuantConnect Stubs for Python Autocomplete
RUN pip install --no-cache-dir quantconnect-stubs

RUN conda install --channel conda-forge pyscipopt

RUN add-apt-repository -y ppa:ubuntu-toolchain-r/test

RUN apt update && apt install -y gcc-11 g++-11