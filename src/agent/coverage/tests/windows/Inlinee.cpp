#include <iostream>

__declspec(dllexport) void test();

int main()
{
    std::cout << "Before\n";
    test();
    std::cout << "After\n";
}

__declspec(dllexport) void test() {
    std::cout << "Hello World!\n";
}
