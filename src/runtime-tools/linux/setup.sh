#!/bin/bash
# Copyright (c) Microsoft Corporation.
# Licensed under the MIT License.


find .
set -x

INSTANCE_OS_SETUP="/onefuzz/instance-specific-setup/linux/setup.sh"
INSTANCE_SETUP="/onefuzz/instance-specific-setup/setup.sh"
USER_SETUP="/onefuzz/setup/setup.sh"
TASK_SETUP="/onefuzz/bin/task-setup.sh"
MANAGED_SETUP="/onefuzz/bin/managed.sh"
SCALESET_SETUP="/onefuzz/bin/scaleset-setup.sh"
export DOTNET_ROOT=/onefuzz/tools/dotnet
export ONEFUZZ_ROOT=/onefuzz
export LLVM_SYMBOLIZER_PATH=/onefuzz/bin/llvm-symbolizer

logger "onefuzz: making directories"
sudo mkdir -p /onefuzz/downloaded
sudo chown -R $(whoami) /onefuzz
mv * /onefuzz/downloaded
cd /onefuzz
mkdir -p /onefuzz/bin
mkdir -p /onefuzz/logs
mkdir -p /onefuzz/setup
mkdir -p /onefuzz/tools
mkdir -p /onefuzz/etc
mkdir -p /onefuzz/instance-specific-setup
mkdir -p /onefuzz/tools/dotnet

echo $1 > /onefuzz/etc/mode
export PATH=$PATH:/onefuzz/bin:/onefuzz/tools/linux:/onefuzz/tools/linux/afl:/onefuzz/tools/linux/radamsa

# Basic setup
mv /onefuzz/downloaded/config.json /onefuzz
mv /onefuzz/downloaded/azcopy /onefuzz/bin
mv /onefuzz/downloaded/managed.sh /onefuzz/bin

if [ -f /onefuzz/downloaded/task-setup.sh ]; then
    mv /onefuzz/downloaded/task-setup.sh /onefuzz/bin/
fi

if [ -f /onefuzz/downloaded/repro.sh ]; then
    mv /onefuzz/downloaded/repro.sh /onefuzz/bin/
fi
if [ -f /onefuzz/downloaded/repro-stdout.sh ]; then
    mv /onefuzz/downloaded/repro-stdout.sh /onefuzz/bin/
fi
if [ -f /onefuzz/downloaded/scaleset-setup.sh ]; then
    mv /onefuzz/downloaded/scaleset-setup.sh /onefuzz/bin
fi

chmod -R a+rx /onefuzz/bin

if [ -f ${MANAGED_SETUP} ]; then
    logger "onefuzz: managed setup script start"
    chmod +x ${MANAGED_SETUP}
    ${MANAGED_SETUP} 2>&1 | logger -s -i -t 'onefuzz-managed-setup'
    logger "onefuzz: managed setup script stop"
else
    logger "onefuzz: no managed setup script"
fi

if [ -f ${SCALESET_SETUP} ]; then
    logger "onefuzz: scaleset setup script start"
    chmod +x ${SCALESET_SETUP}
    ${SCALESET_SETUP} 2>&1 | logger -s -i -t 'onefuzz-scaleset-setup'
    logger "onefuzz: scaleset setup script stop"
else
    logger "onefuzz: no scaleset setup script"
fi

if [ -f ${INSTANCE_SETUP} ]; then
    logger "onefuzz: instance setup script start"
    chmod +x ${INSTANCE_SETUP}
    ${INSTANCE_SETUP} 2>&1 | logger -s -i -t 'onefuzz-instance-setup'
    logger "onefuzz: instance setup script stop"
elif [ -f ${INSTANCE_OS_SETUP} ]; then
    logger "onefuzz: instance setup script (linux) start"
    chmod +x ${INSTANCE_OS_SETUP}
    ${INSTANCE_OS_SETUP} 2>&1 | logger -s -i -t 'onefuzz-instance-setup'
    logger "onefuzz: instance setup script stop"
else
    logger "onefuzz: no instance setup script"
fi

# When repro case is moved into the supervisor, this should be deleted
if [ -f ${TASK_SETUP} ]; then
    logger "onefuzz: task-specific setup script start"
    chmod +x ${TASK_SETUP}
    ${TASK_SETUP} 2>&1 | logger -s -i -t 'onefuzz-task-setup'
    logger "onefuzz: task-specific setup script stop"
else
    logger "onefuzz: no task-specific setup script"
fi


if [ -f ${USER_SETUP} ]; then
    logger "onefuzz: user-specific setup script start"
    chmod +x ${USER_SETUP}
    ${USER_SETUP} 2>&1 | logger -s -i -t 'onefuzz-user-setup'
    logger "onefuzz: user-specific setup script stop"
else
    logger "onefuzz: no user-specific setup script"
fi

chmod -R a+rx /onefuzz/tools/linux

if type apt > /dev/null 2> /dev/null; then
    sudo apt update
    until sudo apt install -y gdb gdbserver; do
        echo "apt failed.  sleep 10s, then retrying"
        sleep 10
    done

    if ! [ -f ${LLVM_SYMBOLIZER_PATH} ]; then
        until sudo apt install -y llvm-10; do
            echo "apt failed, sleeping 10s then retrying"
            sleep 10
        done

        # If specifying symbolizer, exe name must be a "known symbolizer".
        # Using `llvm-symbolizer` works for clang 8 .. 10.
        sudo ln -f -s $(which llvm-symbolizer-10) $LLVM_SYMBOLIZER_PATH
    fi

    # Install dotnet
    until sudo apt install -y curl libicu-dev; do
        echo "apt failed, sleeping 10s then retrying"
        sleep 10
    done

    echo "downloading dotnet install"
    curl -sSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh > /dev/null
    chmod +x dotnet-install.sh

    echo "running dotnet install"
    . ./dotnet-install.sh --version 6.0.300 --install-dir /onefuzz/tools/dotnet 2>&1 | logger -s -i -t 'onefuzz-dotnet-setup'
    rm dotnet-install.sh

    echo "install dotnet tools"
    pushd /onefuzz/tools/dotnet
    ./dotnet tool install dotnet-dump --tool-path /onefuzz/tools
    ./dotnet tool install dotnet-coverage --tool-path /onefuzz/tools
    ./dotnet tool install dotnet-sos --tool-path /onefuzz/tools
    popd
fi

if [ -d /etc/systemd/system ]; then
    logger "onefuzz: setting up systemd"
    sudo chmod 644 /onefuzz/tools/linux/onefuzz.service
    sudo chown root /onefuzz/tools/linux/onefuzz.service
    sudo ln -s /onefuzz/tools/linux/onefuzz.service /etc/systemd/system/onefuzz.service
    sudo systemctl enable onefuzz
    if [ "X$2" == "Xreboot" ]; then
        logger "onefuzz: restarting"
        echo rebooting
        sudo reboot
    else
        logger "onefuzz: starting via systemd"
        sudo systemctl start onefuzz
    fi
elif [ -d /etc/init.d ]; then
    logger "onefuzz: setting up init.d"
    sudo chown root /onefuzz/tools/linux/onefuzz.initd
    sudo ln -s /onefuzz/tools/linux/onefuzz.initd /etc/init.d/onefuzz
    RCDIRS=/etc/rc2.d /etc/rc3.d /etc/rc4.d /etc/rc5.d
    for RCDIR in ${RCDRS}; do
        if [ -d ${RCDIR} ]; then
            sudo ln -s /onefuzz/tools/linux/onefuzz.initd ${RCDIR}/S99onefuzz
        fi
    done
    if [ "X$2" == "Xreboot" ]; then
        logger "onefuzz: rebooting"
        sudo reboot
    else
        logger "onefuzz: starting via init"
        sudo /etc/init.d/onefuzz start
    fi
else
    logger "onefuzz: unknown startup"
    if [ "X$2" == "Xreboot" ]; then
        logger "onefuzz: rebooting without startup script"
        sudo reboot
    else
        logger "onefuzz: starting directly"
        nohup sudo /onefuzz/tools/linux/run.sh
    fi
fi
